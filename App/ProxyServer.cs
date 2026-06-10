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

    // ── 流量统计 ──
    private long _totalBytesReceived;
    private long _totalBytesSent;
    private readonly ConcurrentQueue<string> _connectionLogs = new();
    private const int MaxConnectionLogs = 500;

    public int Port => _port;
    public bool IsRunning => _running;
    public long TotalConnections => _stats.GetValueOrDefault("total", 0);
    public long ActiveConnections => _stats.GetValueOrDefault("active", 0);
    public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);
    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);

    public TrafficStatsDto GetTrafficStats()
    {
        return new TrafficStatsDto(
            TotalConnections,
            ActiveConnections,
            TotalBytesReceived,
            TotalBytesSent,
            _connectionLogs.ToList()
        );
    }

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
                var targetHost = "";
                var startTime = DateTime.UtcNow;

                if (method == "CONNECT")
                {
                    var hostPort = url;
                    var hp = hostPort.Split(':');
                    targetHost = hp[0];
                    var connStart = DateTime.UtcNow;
                    var (sent, received) = await HandleConnectWithStatsAsync(stream, hostPort, buffer, ct);
                    var elapsed = (DateTime.UtcNow - connStart).TotalMilliseconds;

                    Interlocked.Add(ref _totalBytesSent, sent);
                    Interlocked.Add(ref _totalBytesReceived, received);

                    AddConnectionLog(targetHost, "CONNECT", sent + received, elapsed, sent + received > 0);
                }
                else
                {
                    targetHost = ExtractHostFromUrl(url, headerData);
                    var httpStart = DateTime.UtcNow;
                    var (sent, received) = await HandleHttpWithStatsAsync(stream, method, url, headerData, buffer, ct);
                    var elapsed = (DateTime.UtcNow - httpStart).TotalMilliseconds;

                    Interlocked.Add(ref _totalBytesSent, sent);
                    Interlocked.Add(ref _totalBytesReceived, received);

                    AddConnectionLog(targetHost, method, sent + received, elapsed, sent + received > 0);
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

    private void AddConnectionLog(string host, string method, long bytes, double ms, bool success)
    {
        var log = $"[{DateTime.Now:HH:mm:ss}] {method} {host} - {(success ? "OK" : "ERR")} {FormatBytes(bytes)} ({ms:F0}ms)";
        _connectionLogs.Enqueue(log);
        while (_connectionLogs.Count > MaxConnectionLogs)
            _connectionLogs.TryDequeue(out _);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
        return $"{bytes / (1024.0 * 1024.0):F1}MB";
    }

    private static string ExtractHostFromUrl(string url, string headers)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;
        var hostLine = headers.Split('\n')
            .FirstOrDefault(l => l.Trim().StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        if (hostLine != null)
            return hostLine["Host:".Length..].Trim().Split(':')[0];
        return url;
    }

    /// <summary>
    /// HandleConnectAsync with byte counting
    /// </summary>
    private async Task<(long sent, long received)> HandleConnectWithStatsAsync(
        Stream clientStream, string hostPort, byte[] sharedBuffer, CancellationToken ct)
    {
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 443;

        var targetIp = ResolveHost(host);

        TcpClient? remoteClient = null;
        try
        {
            remoteClient = new TcpClient();
            remoteClient.NoDelay = true;
            await remoteClient.ConnectAsync(targetIp, port, ct);
        }
        catch when (targetIp != host)
        {
            remoteClient?.Dispose();
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                var dnsIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
                if (dnsIp == null || dnsIp == targetIp) throw;
                
                remoteClient = new TcpClient();
                remoteClient.NoDelay = true;
                await remoteClient.ConnectAsync(dnsIp, port, ct);
            }
            catch
            {
                remoteClient?.Dispose();
                var errorResponse = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n");
                try { await clientStream.WriteAsync(errorResponse, ct); } catch { }
                return (0, 0);
            }
        }

        try
        {
            using (remoteClient)
            {
                var remoteStream = remoteClient.GetStream();

                var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                await clientStream.WriteAsync(response, ct);
                await clientStream.FlushAsync(ct);

                // 双向转发带计数
                return await CountedBidirectionalCopyAsync(clientStream, remoteStream, ct);
            }
        }
        catch
        {
            var errorResponse = Encoding.ASCII.GetBytes(
                "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n");
            try { await clientStream.WriteAsync(errorResponse, ct); } catch { }
            return (0, 0);
        }
    }

    /// <summary>
    /// HandleHttpAsync with byte counting
    /// </summary>
    private async Task<(long sent, long received)> HandleHttpWithStatsAsync(
        Stream clientStream, string method, string url, string headers, byte[] buffer, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var hostHeader = headers.Split('\n')
                .FirstOrDefault(l => l.Trim().StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                ?.Trim();
            if (hostHeader != null)
            {
                var hostValue = hostHeader["Host:".Length..].Trim();
                if (!Uri.TryCreate($"http://{hostValue}{url}", UriKind.Absolute, out uri))
                {
                    SendError(clientStream, 400, "Bad Request");
                    return (0, 0);
                }
            }
            else
            {
                SendError(clientStream, 400, "Bad Request");
                return (0, 0);
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

            var modifiedHeaders = ReplaceHostInHeaders(headers, targetHost);
            var pathAndQuery = uri.PathAndQuery;
            var firstLineEnd = modifiedHeaders.IndexOf('\n');
            var requestLine = $"{method} {pathAndQuery} HTTP/1.1";
            modifiedHeaders = requestLine + modifiedHeaders[firstLineEnd..];

            var headerBytes = Encoding.ASCII.GetBytes(modifiedHeaders);
            await remoteStream.WriteAsync(headerBytes, ct);
            await remoteStream.FlushAsync(ct);

            long sent = headerBytes.Length;
            var (s, r) = await CountedSingleDirectionCopyAsync(remoteStream, clientStream, ct);
            return (sent + s, r);
        }
        catch
        {
            SendError(clientStream, 502, "Bad Gateway");
            return (0, 0);
        }
    }

    private string ResolveHost(string host)
    {
        if (_hostMappings.TryGetValue(host, out var ip))
        {
            return ip;
        }
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
            if (sb.ToString().Contains("\r\n\r\n"))
                break;
        }

        var data = sb.ToString();
        var headerEnd = data.IndexOf("\r\n\r\n");
        if (headerEnd >= 0)
            return data[..(headerEnd + 4)];
        return data;
    }

    /// <summary>
    /// 双向转发（CONNECT隧道），带字节计数
    /// </summary>
    private static async Task<(long sent, long received)> CountedBidirectionalCopyAsync(
        Stream client, Stream remote, CancellationToken ct)
    {
        long sent = 0, received = 0;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var clientToRemote = CountedCopyStreamAsync(client, remote, cts.Token, v => Interlocked.Add(ref sent, v));
        var remoteToClient = CountedCopyStreamAsync(remote, client, cts.Token, v => Interlocked.Add(ref received, v));

        await Task.WhenAny(clientToRemote, remoteToClient);
        cts.Cancel();

        // 等待另一个方向也完成
        try { await Task.WhenAll(clientToRemote, remoteToClient); } catch { }

        return (sent, received);
    }

    /// <summary>
    /// 单向转发（HTTP代理响应），带字节计数
    /// </summary>
    private static async Task<(long sent, long received)> CountedSingleDirectionCopyAsync(
        Stream source, Stream destination, CancellationToken ct)
    {
        long total = 0;
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                await destination.FlushAsync(ct);
                total += read;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        return (0, total);
    }

    private static async Task CountedCopyStreamAsync(Stream source, Stream destination,
        CancellationToken ct, Action<long> onBytes)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                await destination.FlushAsync(ct);
                onBytes(read);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
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
