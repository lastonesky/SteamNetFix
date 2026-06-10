using System.Runtime.InteropServices;
using System.Security;

namespace SteamNetFix.App;

/// <summary>
/// Hosts文件管理器 - 负责读写系统hosts文件
/// </summary>
public class HostsManager
{
    private static readonly string HostsPath = GetHostsPath();
    private const string MarkerStart = "# >>> SteamNetFix START >>>";
    private const string MarkerEnd = "# <<< SteamNetFix END <<<";

    private static string GetHostsPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"System32\drivers\etc\hosts");
        }
        // Linux / macOS
        return "/etc/hosts";
    }

    /// <summary>
    /// 获取hosts文件路径
    /// </summary>
    public static string GetHostsFilePath() => HostsPath;

    /// <summary>
    /// 检查hosts文件是否可写（需要管理员/root权限）
    /// </summary>
    public bool CanWrite()
    {
        try
        {
            if (!File.Exists(HostsPath)) return false;
            
            using var fs = File.Open(HostsPath, FileMode.Open, FileAccess.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查当前进程是否有管理员权限
    /// </summary>
    public static bool IsRunningAsAdmin()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
        else
        {
            // Unix: check if running as root (UID 0)
            return Environment.GetEnvironmentVariable("SUDO_UID") != null ||
                   getuid() == 0;
        }
    }

    [DllImport("libc")]
    private static extern uint getuid();

    /// <summary>
    /// 读取hosts文件的原始内容（不含SteamNetFix段）
    /// </summary>
    public string ReadOriginalHosts()
    {
        try
        {
            if (!File.Exists(HostsPath)) return "";
            var content = File.ReadAllText(HostsPath);
            return RemoveSteamNetFixSection(content);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Hosts] 读取hosts文件失败: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 读取当前由SteamNetFix管理的hosts条目
    /// </summary>
    public Dictionary<string, string> ReadManagedEntries()
    {
        var entries = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(HostsPath)) return entries;
            var content = File.ReadAllText(HostsPath);
            var section = ExtractSteamNetFixSection(content);
            if (string.IsNullOrEmpty(section)) return entries;

            foreach (var line in section.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    entries[parts[1]] = parts[0];
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Hosts] 读取managed entries失败: {ex.Message}");
        }
        return entries;
    }

    /// <summary>
    /// 应用加速规则到hosts文件
    /// </summary>
    public bool Apply(Dictionary<string, string> domainToIp)
    {
        try
        {
            if (!File.Exists(HostsPath))
            {
                Console.Error.WriteLine("[Hosts] hosts文件不存在");
                return false;
            }

            var originalContent = File.ReadAllText(HostsPath);
            var cleanContent = RemoveSteamNetFixSection(originalContent);

            if (domainToIp.Count == 0)
            {
                File.WriteAllText(HostsPath, cleanContent.TrimEnd() + "\n");
                return true;
            }

            // 生成SteamNetFix段
            var section = new System.Text.StringBuilder();
            section.AppendLine();
            section.AppendLine(MarkerStart);
            section.AppendLine("# SteamNetFix 网络加速 - 请勿手动修改");
            section.AppendLine($"# 更新时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            section.AppendLine();
            
            foreach (var (domain, ip) in domainToIp.OrderBy(kv => kv.Key))
            {
                section.AppendLine($"{ip,-20} {domain}");
            }
            
            section.AppendLine();
            section.AppendLine(MarkerEnd);
            section.AppendLine();

            File.WriteAllText(HostsPath, cleanContent.TrimEnd() + section.ToString());
            
            Console.WriteLine($"[Hosts] 已写入 {domainToIp.Count} 条加速规则");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("[Hosts] 权限不足，无法写入hosts文件。请以管理员/root权限运行。");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Hosts] 写入hosts文件失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 清除所有SteamNetFix的hosts条目
    /// </summary>
    public bool Clear()
    {
        return Apply(new Dictionary<string, string>());
    }

    /// <summary>
    /// 刷新系统DNS缓存
    /// </summary>
    public static void FlushDnsCache()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                RunProcess("ipconfig", "/flushdns");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                RunProcess("dscacheutil", "-flushcache");
                RunProcess("killall", "-HUP mDNSResponder");
            }
            else // Linux
            {
                // systemd-resolved
                RunProcess("systemctl", "restart systemd-resolved");
                // 或 nscd
                RunProcess("nscd", "-i hosts");
            }
            Console.WriteLine("[DNS] DNS缓存已刷新");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DNS] 刷新DNS缓存失败: {ex.Message}");
        }
    }

    private static void Process_OutputDataReceived(object sender, System.Diagnostics.DataReceivedEventArgs e)
    {
        // intentionally empty - suppress output
    }

    private static void RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // 静默失败
        }
    }

    private static string ExtractSteamNetFixSection(string content)
    {
        var startIdx = content.IndexOf(MarkerStart);
        var endIdx = content.IndexOf(MarkerEnd);
        
        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
            return "";

        return content[(startIdx + MarkerStart.Length)..endIdx];
    }

    private static string RemoveSteamNetFixSection(string content)
    {
        var startIdx = content.IndexOf(MarkerStart);
        var endIdx = content.IndexOf(MarkerEnd);
        
        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
            return content;

        // 找到MarkerEnd之后的换行符
        var afterEnd = endIdx + MarkerEnd.Length;
        if (afterEnd < content.Length && content[afterEnd] == '\n')
            afterEnd++;
        if (afterEnd < content.Length && content[afterEnd] == '\r')
            afterEnd++;

        return content[..startIdx] + content[afterEnd..];
    }
}
