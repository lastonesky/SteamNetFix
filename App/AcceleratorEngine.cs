using System.Text.Json;

namespace SteamNetFix.App;

/// <summary>
/// 加速引擎 - 统一管理所有加速功能
/// </summary>
public class AcceleratorEngine : IDisposable
{
    private readonly AppConfig _config;
    private readonly HostsManager _hostsManager;
    private readonly IpTester _ipTester;
    private ProxyServer? _proxyServer;
    private Timer? _autoTestTimer;
    private Timer? _autoRefreshTimer;
    
    public AppConfig Config => _config;
    public bool IsAccelerating { get; private set; }
    public DateTime? LastSpeedTest => _config.LastSpeedTest;
    public Dictionary<string, string> CurrentMappings => new(_config.SelectedIps);
    
    // 事件
    public event Action<string>? OnLog;
    public event Action<bool>? OnAccelerationStateChanged;

    public AcceleratorEngine(AppConfig config)
    {
        _config = config;
        _hostsManager = new HostsManager();
        _ipTester = new IpTester(config.IpTestTimeoutMs);
    }

    /// <summary>
    /// 初始化引擎
    /// </summary>
    public async Task InitializeAsync()
    {
        Log("正在初始化加速引擎...");
        
        // 检查权限
        if (!HostsManager.IsRunningAsAdmin())
        {
            Log("⚠ 警告: 未以管理员/root权限运行，hosts文件可能无法修改");
        }

        // 清除上次可能残留的系统代理（防止崩溃后代理残留导致断网）
        if (_config.SetSystemProxy)
        {
            SystemProxyManager.ClearProxy();
        }
        
        // 加载上次的最优IP
        if (_config.SelectedIps.Count > 0 && _config.Enabled)
        {
            Log("正在恢复上次加速状态...");
            var (success, message) = await StartAccelerateAsync();
            if (!success)
                Log($"⚠ 恢复加速状态失败: {message}");
        }
        
        // 设置自动测速
        if (_config.AutoTestIntervalHours > 0)
        {
            _autoTestTimer = new Timer(
                async _ => await RunSpeedTestAsync(),
                null,
                TimeSpan.FromHours(_config.AutoTestIntervalHours),
                TimeSpan.FromHours(_config.AutoTestIntervalHours));
        }
        
        Log("加速引擎初始化完成");
    }

    /// <summary>
    /// 获取所有可用服务
    /// </summary>
    public List<ServiceDefinition> GetAvailableServices()
    {
        return BuiltinRules.GetAll();
    }

    /// <summary>
    /// 获取已启用的服务
    /// </summary>
    public List<ServiceDefinition> GetEnabledServices()
    {
        return BuiltinRules.GetAll()
            .Where(s => _config.EnabledServices.Contains(s.Id))
            .ToList();
    }

    /// <summary>
    /// 启用/禁用指定服务
    /// </summary>
    public void ToggleService(string serviceId, bool enabled)
    {
        if (enabled)
            _config.EnabledServices.Add(serviceId);
        else
            _config.EnabledServices.Remove(serviceId);
        
        _config.Save();
        Log($"服务 {serviceId} 已{(enabled ? "启用" : "禁用")}");
    }

    /// <summary>
    /// 运行IP测速
    /// </summary>
    public async Task RunSpeedTestAsync()
    {
        var enabledServices = GetEnabledServices();
        if (enabledServices.Count == 0)
        {
            Log("没有启用任何加速服务");
            return;
        }

        Log("正在测速...");
        
        var bestIps = await _ipTester.TestAllRulesAsync(enabledServices, 
            new Progress<string>(msg => Log(msg)));
        
        _config.SelectedIps = bestIps;
        _config.LastSpeedTest = DateTime.UtcNow;
        _config.Save();
        
        Log($"测速完成，已选择 {bestIps.Count} 个最优IP");
        
        // 如果正在加速，立即应用新的IP
        if (IsAccelerating)
        {
            await ApplyHosts();
        }
    }

    /// <summary>
    /// 开始加速
    /// </summary>
    public async Task<(bool Success, string Message)> StartAccelerateAsync()
    {
        if (IsAccelerating) return (true, "加速已在运行中");
        
        // 如果没有测速结果，先测速
        if (_config.SelectedIps.Count == 0)
        {
            Log("首次使用，正在测速选择最优IP...");
            await RunSpeedTestAsync();
            
            if (_config.SelectedIps.Count == 0)
            {
                var msg = "测速未获得有效IP，请检查网络连接后重试";
                Log($"❌ {msg}");
                return (false, msg);
            }
        }

        // 应用hosts
        if (!await ApplyHosts())
        {
            var msg = "应用hosts失败，请确认已以管理员权限运行";
            Log($"❌ {msg}");
            return (false, msg);
        }

        // 启动代理服务器（如果启用）
        if (_config.ProxyEnabled)
        {
            try
            {
                await StartProxyAsync();
            }
            catch (Exception ex)
            {
                var msg = $"代理服务器启动失败: {ex.Message}";
                Log($"❌ {msg}");
                return (false, msg);
            }

            // 验证代理是否真正可用
            var proxyOk = await VerifyProxyAsync(_config.ProxyPort);
            if (!proxyOk)
            {
                var msg = $"代理服务器已启动但无法连接 (端口 {_config.ProxyPort})";
                Log($"⚠ {msg}");
                return (false, msg);
            }

            // 设置系统代理（让浏览器等应用流量经过加速器）
            if (_config.SetSystemProxy)
            {
                if (SystemProxyManager.SetProxy(_config.ProxyPort))
                    Log($"已设置系统代理 → 127.0.0.1:{_config.ProxyPort}");
                else
                    Log("⚠ 设置系统代理失败，部分应用可能无法自动走代理通道");
            }
        }

        IsAccelerating = true;
        _config.Enabled = true;
        _config.Save();
        
        OnAccelerationStateChanged?.Invoke(true);
        Log("✅ 加速已启动");
        return (true, "加速已成功启动");
    }

    /// <summary>
    /// 验证代理服务器是否可连接
    /// </summary>
    private static async Task<bool> VerifyProxyAsync(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 停止加速
    /// </summary>
    public async Task StopAccelerateAsync()
    {
        if (!IsAccelerating) return;

        // 清除hosts
        _hostsManager.Clear();
        HostsManager.FlushDnsCache();

        // 清除系统代理
        if (_config.SetSystemProxy)
        {
            SystemProxyManager.ClearProxy();
            Log("已清除系统代理");
        }

        // 停止代理
        _proxyServer?.Stop();

        IsAccelerating = false;
        _config.Enabled = false;
        _config.Save();
        
        OnAccelerationStateChanged?.Invoke(false);
        await Task.CompletedTask;
        Log("⏹ 加速已停止");
    }

    /// <summary>
    /// 获取加速状态信息
    /// </summary>
    public AccelerationStatusDto GetStatus()
    {
        var enabledServices = GetEnabledServices();
        return new AccelerationStatusDto(
            IsAccelerating: IsAccelerating,
            EnabledServiceCount: enabledServices.Count,
            EnabledServices: enabledServices.Select(s => new EnabledServiceDto(s.Id, s.Name, s.Icon)).ToList(),
            SelectedIpCount: _config.SelectedIps.Count,
            SelectedIps: _config.SelectedIps,
            LastSpeedTest: _config.LastSpeedTest,
            ProxyEnabled: _config.ProxyEnabled,
            ProxyPort: _config.ProxyPort,
            ProxyRunning: _proxyServer?.IsRunning ?? false,
            ProxyConnections: _proxyServer?.TotalConnections ?? 0,
            IsAdmin: HostsManager.IsRunningAsAdmin(),
            HostsPath: HostsManager.GetHostsFilePath(),
            WebPort: _config.WebPort,
            AutoTestIntervalHours: _config.AutoTestIntervalHours
        );
    }

    /// <summary>
    /// 获取当前hosts中的加速条目
    /// </summary>
    public Dictionary<string, string> GetHostsEntries()
    {
        return _hostsManager.ReadManagedEntries();
    }

    /// <summary>
    /// 手动刷新DNS缓存
    /// </summary>
    public void FlushDns()
    {
        HostsManager.FlushDnsCache();
        Log("DNS缓存已刷新");
    }

    /// <summary>
    /// 重新测速并应用
    /// </summary>
    public async Task RetestAsync()
    {
        await RunSpeedTestAsync();
        if (IsAccelerating)
        {
            await ApplyHosts();
        }
    }

    private async Task<bool> ApplyHosts()
    {
        // 只启用已选择服务的域名
        var enabledServices = GetEnabledServices();
        if (enabledServices.Count == 0)
        {
            Log("没有启用任何加速服务");
            return false;
        }

        var mappings = new Dictionary<string, string>();
        bool hasProxyDomains = false;
        
        foreach (var service in enabledServices)
        {
            foreach (var rule in service.Domains)
            {
                if (rule.UseProxy)
                {
                    hasProxyDomains = true;
                    continue;
                }

                if (_config.SelectedIps.TryGetValue(rule.Domain, out var ip))
                {
                    mappings[rule.Domain] = ip;
                }
            }
        }

        if (mappings.Count == 0)
        {
            if (hasProxyDomains)
            {
                // 所有域名都通过代理转发，无需写入hosts（这是正常情况）
                Log("所有域名通过代理转发，跳过hosts写入");
                return true;
            }
            Log("没有可用的IP映射");
            return false;
        }

        var result = _hostsManager.Apply(mappings);
        if (result)
        {
            HostsManager.FlushDnsCache();
            Log($"已应用 {mappings.Count} 条hosts规则");
        }
        return result;
    }

    private async Task StartProxyAsync()
    {
        if (_proxyServer?.IsRunning == true) return;

        // 将所有已选择的最优IP传入代理，确保代理模式下的域名也能获得加速
        // （不再过滤 UseProxy=true 的域名，因为那些域名也需要优化IP而非回退到系统DNS）
        var allSelectedIps = new Dictionary<string, string>(_config.SelectedIps);

        _proxyServer = new ProxyServer(_config.ProxyPort, allSelectedIps);
        await _proxyServer.StartAsync();
        Log($"代理服务器已启动 (127.0.0.1:{_config.ProxyPort})");
    }

    /// <summary>
    /// 获取代理流量统计
    /// </summary>
    public TrafficStatsDto? GetTrafficStats()
    {
        return _proxyServer?.GetTrafficStats();
    }

    private void Log(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(logLine);
        OnLog?.Invoke(logLine);
    }

    public void Dispose()
    {
        _autoTestTimer?.Dispose();
        _autoRefreshTimer?.Dispose();
        _proxyServer?.Dispose();
    }
}
