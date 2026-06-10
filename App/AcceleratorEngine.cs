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
        
        // 加载上次的最优IP
        if (_config.SelectedIps.Count > 0 && _config.Enabled)
        {
            Log("正在恢复上次加速状态...");
            await StartAccelerateAsync();
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
    public async Task StartAccelerateAsync()
    {
        if (IsAccelerating) return;
        
        // 如果没有测速结果，先测速
        if (_config.SelectedIps.Count == 0)
        {
            Log("首次使用，正在测速选择最优IP...");
            await RunSpeedTestAsync();
        }

        // 应用hosts
        if (!await ApplyHosts())
        {
            Log("❌ 应用hosts失败");
            return;
        }

        // 启动代理服务器（如果启用）
        if (_config.ProxyEnabled)
        {
            await StartProxyAsync();
        }

        IsAccelerating = true;
        _config.Enabled = true;
        _config.Save();
        
        OnAccelerationStateChanged?.Invoke(true);
        Log("✅ 加速已启动");
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
    public object GetStatus()
    {
        var enabledServices = GetEnabledServices();
        return new
        {
            isAccelerating = IsAccelerating,
            enabledServiceCount = enabledServices.Count,
            enabledServices = enabledServices.Select(s => new { s.Id, s.Name, s.Icon }).ToList(),
            selectedIpCount = _config.SelectedIps.Count,
            selectedIps = _config.SelectedIps,
            lastSpeedTest = _config.LastSpeedTest,
            proxyEnabled = _config.ProxyEnabled,
            proxyPort = _config.ProxyPort,
            proxyRunning = _proxyServer?.IsRunning ?? false,
            proxyConnections = _proxyServer?.TotalConnections ?? 0,
            isAdmin = HostsManager.IsRunningAsAdmin(),
            hostsPath = HostsManager.GetHostsFilePath(),
            webPort = _config.WebPort,
            autoTestIntervalHours = _config.AutoTestIntervalHours,
        };
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
        var mappings = new Dictionary<string, string>();
        
        foreach (var service in enabledServices)
        {
            foreach (var rule in service.Domains)
            {
                if (_config.SelectedIps.TryGetValue(rule.Domain, out var ip))
                {
                    mappings[rule.Domain] = ip;
                }
            }
        }

        if (mappings.Count == 0)
        {
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

        _proxyServer = new ProxyServer(_config.ProxyPort, _config.SelectedIps);
        await _proxyServer.StartAsync();
        Log($"代理服务器已启动 (127.0.0.1:{_config.ProxyPort})");
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
