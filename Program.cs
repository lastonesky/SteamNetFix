using System.Diagnostics;
using System.Runtime.InteropServices;
using SteamNetFix.App;

namespace SteamNetFix;

public class Program
{
    private static AcceleratorEngine? _engine;
    private static SystemTray? _tray;
    private static bool _running = true;

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        PrintBanner();
        
        // 解析命令行参数
        var config = AppConfig.Load();
        ParseArgs(args, config);

        // 创建加速引擎
        _engine = new AcceleratorEngine(config);
        
        // 注册日志
        _engine.OnLog += msg =>
        {
            Console.WriteLine(msg);
            LogBuffer.Add(msg);
        };

        // 初始化引擎
        await _engine.InitializeAsync();

        // 启动Web服务器
        var webApp = WebApi.CreateWebApp(_engine, config.WebPort);
        
        // 注册关闭事件
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Shutdown();
        };

        // 初始化系统托盘
        if (SystemTray.IsPlatformSupported)
        {
            _tray = new SystemTray(_engine, config.WebPort, () =>
            {
                Shutdown();
                Environment.Exit(0);
            });
            _ = _tray.StartAsync();
        }

        // 输出启动信息
        Console.WriteLine();
        Console.WriteLine($"  🌐 Web 管理界面: http://127.0.0.1:{config.WebPort}");
        if (config.ProxyEnabled)
        {
            Console.WriteLine($"  🔌 本地代理: 127.0.0.1:{config.ProxyPort}");
        }
        Console.WriteLine($"  ⚙️  配置目录: {AppConfig.GetConfigDirectory()}");
        Console.WriteLine();
        
        if (!HostsManager.IsRunningAsAdmin())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ⚠  当前未以管理员/root权限运行");
            Console.WriteLine("     hosts文件修改功能将不可用");
            Console.WriteLine("     请以管理员权限重新运行本程序");
            Console.ResetColor();
            Console.WriteLine();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ✅ 已获取管理员权限");
            Console.ResetColor();
            Console.WriteLine();
        }

        Console.WriteLine("  按 Ctrl+C 退出程序");
        Console.WriteLine("  ─────────────────────────────────────");
        Console.WriteLine();

        // 自动打开浏览器
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                OpenBrowser($"http://127.0.0.1:{config.WebPort}");
            }
            catch { }
        });

        // 运行Web服务器
        try
        {
            await webApp.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Web服务器启动失败: {ex.Message}");
        }
    }

    private static void ParseArgs(string[] args, AppConfig config)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" or "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var port))
                        config.WebPort = port;
                    break;
                case "--proxy-port":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var proxyPort))
                        config.ProxyPort = proxyPort;
                    break;
                case "--no-proxy":
                    config.ProxyEnabled = false;
                    break;
                case "--auto-start":
                    config.Enabled = true;
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
            }
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   _____ _______  __  __ _   _ _____   _____ _______        ______ _   _ _____  
  / ____|__  /\ \/ / | \ | | \ | |  __ \_   _|__  /\ \      / / ___| \ | |_   _| 
  \___ \  / /  \  /  |  \| |  \| | |  | || |   / /  \ \ /\ / / |  _|  \| | | |   
   ___) |/ /__ /  \  | . ` | . ` | |  | || |  / /__  \ V  V /| |_| | . ` | | |   
  |____//_____/_/\_\ |_|\_\_|\_|____/ |_| /_____|  \_/\_/  \____|_|\_| |_|   
");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  跨平台网络加速工具 v1.0.0 | 无广告 · 开源 · 跨平台");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: SteamNetFix [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --port, -p <port>      Web UI 端口 (默认: 2606)");
        Console.WriteLine("  --proxy-port <port>    代理端口 (默认: 2607)");
        Console.WriteLine("  --no-proxy             禁用代理服务器");
        Console.WriteLine("  --auto-start           启动时自动开启加速");
        Console.WriteLine("  --help, -h             显示帮助");
    }

    private static void Shutdown()
    {
        if (!_running) return;
        _running = false;
        
        Console.WriteLine();
        Console.WriteLine("正在停止加速...");
        
        _tray?.Dispose();
        _engine?.StopAccelerateAsync().Wait();
        _engine?.Dispose();
        
        Console.WriteLine("程序已退出");
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            Console.WriteLine($"请手动打开浏览器访问: {url}");
        }
    }
}
