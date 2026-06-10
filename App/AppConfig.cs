using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamNetFix.App;

/// <summary>
/// 应用配置
/// </summary>
public class AppConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamNetFix");
    
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    /// <summary>是否启用加速</summary>
    public bool Enabled { get; set; }

    /// <summary>已启用的加速服务列表</summary>
    public HashSet<string> EnabledServices { get; set; } = new() { "steam", "github" };

    /// <summary>Web UI 端口</summary>
    public int WebPort { get; set; } = 2606;

    /// <summary>是否启用代理模式</summary>
    public bool ProxyEnabled { get; set; } = true;

    /// <summary>代理端口</summary>
    public int ProxyPort { get; set; } = 2607;

    /// <summary>自定义DNS服务器</summary>
    public string? CustomDns { get; set; }

    /// <summary>IP测速超时(毫秒)</summary>
    public int IpTestTimeoutMs { get; set; } = 3000;

    /// <summary>上次测速时间</summary>
    public DateTime? LastSpeedTest { get; set; }

    /// <summary>已选择的最优IP映射 (domain -> ip)</summary>
    public Dictionary<string, string> SelectedIps { get; set; } = new();

    /// <summary>自动测速间隔(小时), 0表示不自动测速</summary>
    public int AutoTestIntervalHours { get; set; } = 24;

    /// <summary>是否开机自启</summary>
    public bool AutoStart { get; set; }

    /// <summary>是否最小化到托盘</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>是否显示HTTP请求日志</summary>
    public bool ShowHttpLogs { get; set; }

    /// <summary>是否自动设置系统代理</summary>
    public bool SetSystemProxy { get; set; } = true;

    /// <summary>使用的hosts源（baked-in/custom）</summary>
    public string HostsSource { get; set; } = "baked-in";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = AppJsonContext.Default,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Config] 加载配置失败: {ex.Message}");
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Config] 保存配置失败: {ex.Message}");
        }
    }

    public static string GetConfigDirectory() => ConfigDir;
}
