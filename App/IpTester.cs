using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SteamNetFix.App;

/// <summary>
/// IP测速结果
/// </summary>
public class IpTestResult
{
    public string Ip { get; set; } = "";
    public string Domain { get; set; } = "";
    public long LatencyMs { get; set; } = -1;
    public bool IsReachable { get; set; }
    public DateTime TestTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// IP测速器 - 测试候选IP的延迟，选择最优IP
/// </summary>
public class IpTester
{
    private readonly int _timeoutMs;
    private readonly int _concurrentTests;
    
    public IpTester(int timeoutMs = 3000, int concurrentTests = 20)
    {
        _timeoutMs = timeoutMs;
        _concurrentTests = concurrentTests;
    }

    /// <summary>
    /// 测试单个IP的延迟 (TCP连接)
    /// </summary>
    public async Task<IpTestResult> TestIpAsync(string ip, string domain, int port = 443)
    {
        var result = new IpTestResult { Ip = ip, Domain = domain };
        var sw = Stopwatch.StartNew();
        
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var timeoutTask = Task.Delay(_timeoutMs);
            
            var completed = await Task.WhenAny(connectTask, timeoutTask);
            
            if (completed == connectTask && !connectTask.IsFaulted)
            {
                sw.Stop();
                result.LatencyMs = sw.ElapsedMilliseconds;
                result.IsReachable = true;
            }
            else
            {
                result.LatencyMs = _timeoutMs;
                result.IsReachable = false;
            }
        }
        catch
        {
            sw.Stop();
            result.LatencyMs = _timeoutMs;
            result.IsReachable = false;
        }

        result.TestTime = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// 测试一个域名的所有候选IP，返回按延迟排序的结果
    /// </summary>
    public async Task<List<IpTestResult>> TestDomainAsync(DomainRule rule)
    {
        var results = new ConcurrentBag<IpTestResult>();
        var semaphore = new SemaphoreSlim(_concurrentTests);
        
        var tasks = rule.CandidateIps.Select(async ip =>
        {
            await semaphore.WaitAsync();
            try
            {
                // 测试3次取平均
                var latencies = new List<long>();
                for (int i = 0; i < 3; i++)
                {
                    var result = await TestIpAsync(ip, rule.Domain, rule.Port);
                    if (result.IsReachable)
                    {
                        latencies.Add(result.LatencyMs);
                    }
                    else
                    {
                        latencies.Add(_timeoutMs);
                        break;
                    }
                }

                var avgLatency = (long)latencies.Average();
                results.Add(new IpTestResult
                {
                    Ip = ip,
                    Domain = rule.Domain,
                    LatencyMs = avgLatency,
                    IsReachable = latencies.Any(l => l < _timeoutMs),
                    TestTime = DateTime.UtcNow,
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results
            .Where(r => r.IsReachable)
            .OrderBy(r => r.LatencyMs)
            .ToList();
    }

    /// <summary>
    /// 批量测试所有加速规则，返回最优IP映射
    /// </summary>
    public async Task<Dictionary<string, string>> TestAllRulesAsync(
        List<ServiceDefinition> services,
        IProgress<string>? progress = null)
    {
        var bestIps = new ConcurrentDictionary<string, string>();
        var allRules = services.SelectMany(s => s.Domains).ToList();
        
        progress?.Report($"开始测速 {allRules.Count} 个域名...");
        
        // 按域名去重
        var uniqueDomains = allRules
            .GroupBy(r => r.Domain)
            .Select(g => g.First())
            .ToList();

        foreach (var rule in uniqueDomains)
        {
            progress?.Report($"测试 {rule.Domain}...");
            
            var results = await TestDomainAsync(rule);
            if (results.Count > 0)
            {
                var best = results.First();
                bestIps[rule.Domain] = best.Ip;
                progress?.Report($"  {rule.Domain} -> {best.Ip} ({best.LatencyMs}ms)");
            }
            else
            {
                progress?.Report($"  {rule.Domain} -> 无可用IP");
            }
        }

        return new Dictionary<string, string>(bestIps);
    }

    /// <summary>
    /// 快速Ping测试（ICMP）
    /// </summary>
    public async Task<bool> PingAsync(string ip, int timeoutMs = 1000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
