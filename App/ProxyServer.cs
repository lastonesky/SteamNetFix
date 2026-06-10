using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SteamNetFix.App;

/// <summary>
/// 本地HTTP/HTTPS代理服务器
/// 支持CONNECT方法（HTTPS隧道）和普通HTTP代理
/// 用于需要流量转发的加速场景
/// </summary>
public class ProxyServer : IDisposable
{
    private readonly int _port;
    private readonly Dictionary<string, string> _hostMappings; // domain -> ip
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, long> _stats = new();
    private bool _running;

    public int Port => _port;
    public bool IsRunning => _running;
    public long TotalConnections => _stats.GetValueOrDefault("total", 0);
    public long ActiveConnections => _stats.GetValueOrDefault("active", 0);

    public ProxyServer(int port, Dictionary<string, string>? hostMappings = null)
    {
        _port = port;
        _hostMappings = hostMappings ?? new Dictionary<string, string>();
    }

    public void UpdateMappings(Dictionary<string, string> mappings)
    {
        foreach (var (key, value) in mappings)
        {
            _hostMappings[key] = value;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _running = true;

        Console.WriteLine($"[Proxy] 本地代理服务器已启动，监听 127.0.0.1:{_port}");

        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client, _cts.Token);
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Proxy] Accept error: {ex.Message}");
                }
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        _listener?.Stop();
        Console.WriteLine("[Proxy] 代理服务器已停止");
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        _stats.AddOrUpdate("total", 1, (_, v) => v + 1);
        _stats.AddOrUpdate("active", 1, (_, v) => v + 1);

        try
        {
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 30000;

                var stream = client.GetStream();
                var buffer = new byte[8192];
                
                // 读取请求头
                var headerData = await ReadHeadersAsync(stream, buffer, ct);
                if (string.IsNullOrEmpty(headerData)) return;

                var firstLine = headerData.Split('\n')[0].Trim();
                var parts = firstLine.Split(' ');
                if (parts.Length < 3) return;

                var method = parts[0].ToUpperInvariant();
                var url = parts[1];

                if (method == "CONNECT")
                {
                    await HandleConnectAsync(stream, url, buffer, ct);
                }
                else
                {
                    await HandleHttpAsync(stream, method, url, headerData, buffer, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 静默处理连接错误
        }
        finally
        {
            _stats.AddOrUpdate("active", 0, (_, v) => Math.Max(0, v - 1));
        }
    }

    private async Task HandleConnectAsync(Stream clientStream, string hostPort, byte[] buffer, CancellationToken ct)
    {
        // host格式: domain:port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 443;

        // 查找是否有IP映射
        var targetIp = ResolveHost(host);

        try
        {
            using var remoteClient = new TcpClient();
            remoteClient.NoDelay = true;
            
            await remoteClient.ConnectAsync(targetIp, port, ct);
            var remoteStream = remoteClient.GetStream();

            // 发送200 Connection Established
            var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await clientStream.WriteAsync(response, ct);
            await clientStream.FlushAsync(ct);

            // 双向转发数据
            await BidirectionalCopyAsync(clientStream, remoteStream, buffer, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var errorResponse = Encoding.ASCII.GetBytes(
                "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n");
            try { await clientStream.WriteAsync(errorResponse, ct); } catch { }
        }
    }

    private async Task HandleHttpAsync(Stream clientStream, string method, string url, 
        string headers, byte[] buffer, CancellationToken ct)
    {
        // 解析URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // 可能是相对URL，需要从Host头获取
            var hostHeader = headers.Split('\n')
                .FirstOrDefault(l => l.Trim().StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                ?.Trim();
            if (hostHeader != null)
            {
                var hostValue = hostHeader["Host:".Length..].Trim();
                if (Uri.TryCreate($"http://{hostValue}{url}", UriKind.Absolute, out uri))
                {
                    // OK
                }
                else
                {
                    SendError(clientStream, 400, "Bad Request");
                    return;
                }
            }
            else
            {
                SendError(clientStream, 400, "Bad Request");
                return;
            }
        }

        var targetHost = uri.Host;
        var targetPort = uri.Port > 0 ? uri.Port : 80;
        var targetIp = ResolveHost(targetHost);

        try
        {
            using var remoteClient = new TcpClient();
            remoteClient.NoDelay = true;
            await remoteClient.ConnectAsync(targetIp, targetPort, ct);
            var remoteStream = remoteClient.GetStream();

            // 修改请求头中的Host
            var modifiedHeaders = ReplaceHostInHeaders(headers, targetHost);
            
            // 修改URL为路径
            var pathAndQuery = uri.PathAndQuery;
            var firstLineEnd = modifiedHeaders.IndexOf('\n');
            var requestLine = $"{method} {pathAndQuery} HTTP/1.1";
            modifiedHeaders = requestLine + modifiedHeaders[firstLineEnd..];

            // 发送修改后的请求
            var headerBytes = Encoding.ASCII.GetBytes(modifiedHeaders);
            await remoteStream.WriteAsync(headerBytes, ct);
            await remoteStream.FlushAsync(ct);

            // 转发响应
            await BidirectionalCopyAsync(clientStream, remoteStream, buffer, ct, isHttp: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SendError(clientStream, 502, "Bad Gateway");
        }
    }

    private string ResolveHost(string host)
    {
        if (_hostMappings.TryGetValue(host, out var ip))
        {
            return ip;
        }
        // 如果没有映射，使用系统DNS
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.FirstOrDefault()?.ToString() ?? host;
        }
        catch
        {
            return host;
        }
    }

    private static async Task<string> ReadHeadersAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var sb = new StringBuilder();
        int totalRead = 0;
        
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) return "";
            
            totalRead += read;
            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            
            // 检查是否读取完所有头部
            if (sb.ToString().Contains("\r\n\r\n"))
                break;
        }

        var data = sb.ToString();
        var headerEnd = data.IndexOf("\r\n\r\n");
        if (headerEnd >= 0)
            return data[..(headerEnd + 4)];
        return data;
    }

    private static async Task BidirectionalCopyAsync(Stream client, Stream remote, 
        byte[] buffer, CancellationToken ct, bool isHttp = false)
    {
        var clientToRemote = CopyStreamAsync(client, remote, buffer, ct);
        var remoteToClient = CopyStreamAsync(remote, client, buffer, ct);
        
        await Task.WhenAny(clientToRemote, remoteToClient);
    }

    private static async Task CopyStreamAsync(Stream source, Stream destination, 
        byte[] buffer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                await destination.FlushAsync(ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // 连接关闭，正常退出
        }
    }

    private static string ReplaceHostInHeaders(string headers, string host)
    {
        var lines = headers.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"Host: {host}";
                break;
            }
        }
        return string.Join('\n', lines);
    }

    private static void SendError(Stream stream, int code, string message)
    {
        var response = $"HTTP/1.1 {code} {message}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(response);
        try { stream.Write(bytes, 0, bytes.Length); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
