using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamNetFix.App;

/// <summary>
/// 系统代理管理器 — 设置/清除操作系统级HTTP代理
/// 支持 Windows（注册表）、macOS（networksetup）、Linux（gsettings）
/// </summary>
public static class SystemProxyManager
{
    /// <summary>
    /// 设置系统代理到指定本地端口
    /// </summary>
    public static bool SetProxy(int port)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return SetProxyWindows(port);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return SetProxyMacOS(port);
            return SetProxyLinux(port);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Proxy] 设置系统代理失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 清除系统代理设置
    /// </summary>
    public static bool ClearProxy()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ClearProxyWindows();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return ClearProxyMacOS();
            return ClearProxyLinux();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Proxy] 清除系统代理失败: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════
    //  Windows
    // ═══════════════════════════════════════════

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416")]
    private static bool SetProxyWindows(int port)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
        if (key == null) return false;

        key.SetValue("ProxyEnable", 1, Microsoft.Win32.RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{port}", Microsoft.Win32.RegistryValueKind.String);
        // 绕过本地地址，避免代理回环
        key.SetValue("ProxyOverride", "localhost;127.*;<local>",
            Microsoft.Win32.RegistryValueKind.String);

        // 通知系统代理设置已变更（让浏览器等立即生效）
        NotifyProxyChangeWindows();
        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416")]
    private static bool ClearProxyWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
        if (key == null) return false;

        key.SetValue("ProxyEnable", 0, Microsoft.Win32.RegistryValueKind.DWord);
        // 保留 ProxyServer 以便下次快速恢复，但关闭代理开关即可

        NotifyProxyChangeWindows();
        return true;
    }

    /// <summary>
    /// 广播 INTERNET_OPTION_SETTINGS_CHANGED，让浏览器等应用立即刷新代理设置
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416")]
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption,
        IntPtr lpBuffer, int dwBufferLength);

    private static void NotifyProxyChangeWindows()
    {
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        const int INTERNET_OPTION_REFRESH = 37;
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }

    // ═══════════════════════════════════════════
    //  macOS
    // ═══════════════════════════════════════════

    private static bool SetProxyMacOS(int port)
    {
        var iface = DetectMacOSNetworkInterface();
        Run("networksetup", $"-setwebproxy \"{iface}\" 127.0.0.1 {port}");
        Run("networksetup", $"-setsecurewebproxy \"{iface}\" 127.0.0.1 {port}");
        // 绕过本地地址
        Run("networksetup", $"-setproxybypassdomains \"{iface}\" localhost 127.0.0.1");
        return true;
    }

    private static bool ClearProxyMacOS()
    {
        var iface = DetectMacOSNetworkInterface();
        Run("networksetup", $"-setwebproxystate \"{iface}\" off");
        Run("networksetup", $"-setsecurewebproxystate \"{iface}\" off");
        return true;
    }

    private static string DetectMacOSNetworkInterface()
    {
        // 尝试获取当前活跃的网络服务
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "networksetup",
                Arguments = "-listallnetworkservices",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(3000);

            foreach (var line in output.Split('\n'))
            {
                var svc = line.Trim();
                if (string.IsNullOrEmpty(svc) || svc.StartsWith("*")) continue;
                // 优先返回 Wi-Fi 或 Ethernet
                if (svc.Contains("Wi-Fi") || svc.Contains("AirPort"))
                    return svc;
            }
            // 回退：返回第一个非星号的服务
            foreach (var line in output.Split('\n'))
            {
                var svc = line.Trim();
                if (!string.IsNullOrEmpty(svc) && !svc.StartsWith("*"))
                    return svc;
            }
        }
        catch { }
        return "Wi-Fi"; // 最终回退
    }

    // ═══════════════════════════════════════════
    //  Linux (GNOME / gsettings)
    // ═══════════════════════════════════════════

    private static bool SetProxyLinux(int port)
    {
        Run("gsettings", "set org.gnome.system.proxy mode 'manual'");
        Run("gsettings", $"set org.gnome.system.proxy.http host '127.0.0.1'");
        Run("gsettings", $"set org.gnome.system.proxy.http port {port}");
        Run("gsettings", $"set org.gnome.system.proxy.https host '127.0.0.1'");
        Run("gsettings", $"set org.gnome.system.proxy.https port {port}");
        Run("gsettings", "set org.gnome.system.proxy ignore-hosts \"['localhost', '127.0.0.1', '::1']\"");
        return true;
    }

    private static bool ClearProxyLinux()
    {
        Run("gsettings", "set org.gnome.system.proxy mode 'none'");
        return true;
    }

    // ═══════════════════════════════════════════
    //  Utilities
    // ═══════════════════════════════════════════

    private static void Run(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch
        {
            // 静默失败
        }
    }
}
