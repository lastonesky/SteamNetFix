using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamNetFix.App;

/// <summary>
/// 跨平台系统托盘集成
/// Windows: 使用 Shell_NotifyIcon (Win32 API)
/// macOS/Linux: 提示使用Web UI
/// </summary>
public class SystemTray : IDisposable
{
    private readonly AcceleratorEngine _engine;
    private readonly int _webPort;
    private Action? _onQuit;

    // Windows tray icon handle
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _isCreated;
    private readonly uint WM_TRAYICON = 0x0400 + 1; // WM_USER + 1

    public SystemTray(AcceleratorEngine engine, int webPort, Action? onQuit = null)
    {
        _engine = engine;
        _webPort = webPort;
        _onQuit = onQuit;
    }

    /// <summary>
    /// 检查当前平台是否支持系统托盘
    /// </summary>
    public static bool IsPlatformSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// 启动系统托盘（异步，在后台线程运行）
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("[Tray] 当前平台不支持系统托盘，请使用 Web 管理界面");
            return Task.CompletedTask;
        }

        return Task.Run(() => RunWindowsTray(ct), ct);
    }

    private void RunWindowsTray(CancellationToken ct)
    {
        try
        {
            // 创建一个隐藏的消息窗口
            if (!CreateHiddenWindow())
            {
                Console.WriteLine("[Tray] 创建系统托盘失败");
                return;
            }

            // 创建托盘图标
            CreateTrayIcon();
            _isCreated = true;

            Console.WriteLine("[Tray] 系统托盘图标已创建");

            // 消息循环
            while (!ct.IsCancellationRequested)
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) // PM_REMOVE
                {
                    if (msg.message == 0x0012) // WM_QUIT
                        return;

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);

                    if (msg.message == WM_TRAYICON)
                    {
                        HandleTrayMessage((int)msg.lParam);
                    }
                }
                Thread.Sleep(50);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Tray] 托盘异常: {ex.Message}");
        }
        finally
        {
            RemoveTrayIcon();
        }
    }

    private bool CreateHiddenWindow()
    {
        var className = "SteamNetFix_TrayWnd";
        
        var wc = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };

        RegisterClass(ref wc);

        _hwnd = CreateWindowEx(
            0, className, "SteamNetFix", 0,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        return _hwnd != IntPtr.Zero;
    }

    private void CreateTrayIcon()
    {
        // 创建一个简单的图标（使用系统默认应用程序图标）
        _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32516); // IDI_APPLICATION

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = $"SteamNetFix - 点击打开管理界面 (端口 {_webPort})",
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private void RemoveTrayIcon()
    {
        if (!_isCreated) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
        };

        Shell_NotifyIcon(NIM_DELETE, ref nid);
        _isCreated = false;
    }

    private void UpdateTooltip(string text)
    {
        if (!_isCreated) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = text.Length >= 128 ? text[..127] : text,
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private void ShowBalloon(string title, string text)
    {
        if (!_isCreated) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_INFO,
            szInfoTitle = title.Length >= 64 ? title[..63] : title,
            szInfo = text.Length >= 256 ? text[..255] : text,
            dwInfoFlags = 1, // NIIF_INFO
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private void HandleTrayMessage(int lParam)
    {
        switch (lParam)
        {
            case 0x0201: // WM_LBUTTONDOWN - 左键点击，打开Web界面
                OpenBrowser($"http://127.0.0.1:{_webPort}");
                break;

            case 0x0204: // WM_RBUTTONDOWN - 右键点击，显示菜单
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        
        // 获取当前状态
        var isRunning = _engine.IsAccelerating;
        
        AppendMenu(hMenu, 0, 100, $"🌐 打开管理界面 (端口 {_webPort})");
        AppendMenu(hMenu, 0, 101, isRunning ? "⏹ 停止加速" : "▶ 开始加速");
        AppendMenu(hMenu, 0, 102, "🔄 重新测速");
        AppendMenu(hMenu, 0x00000800, 0, null); // MF_SEPARATOR
        AppendMenu(hMenu, 0, 103, "❌ 退出");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        
        var cmd = TrackPopupMenu(hMenu, 0x0100, pt.x, pt.y, 0, _hwnd, IntPtr.Zero); // TPM_RETURNCMD
        
        switch (cmd)
        {
            case 100:
                OpenBrowser($"http://127.0.0.1:{_webPort}");
                break;
            case 101:
                if (isRunning)
                    _engine.StopAccelerateAsync().Wait();
                else
                    _engine.StartAccelerateAsync().Wait();
                UpdateTooltip(isRunning ? "SteamNetFix - 加速已停止" : "SteamNetFix - 加速中");
                ShowBalloon("SteamNetFix", isRunning ? "加速已停止" : "加速已启动");
                break;
            case 102:
                _ = _engine.RetestAsync();
                ShowBalloon("SteamNetFix", "正在重新测速...");
                break;
            case 103:
                RemoveTrayIcon();
                _onQuit?.Invoke();
                PostMessage(_hwnd, 0x0010, 0, 0); // WM_CLOSE
                Environment.Exit(0);
                break;
        }

        DestroyMenu(hMenu);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    public void Dispose()
    {
        RemoveTrayIcon();
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    #region Win32 API

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly WndProc _wndProc = WndProcCallback;
    private static IntPtr WndProcCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion
}
