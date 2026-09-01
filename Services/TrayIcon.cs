using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AudioSwitcher.Services;

/// <summary>
/// 原生系统托盘图标（Shell_NotifyIcon）。
/// 消息由 WPF 宿主窗口的 HwndSource 钩子接收，与 WPF 消息循环完全兼容：
/// 不产生独立窗口，不会在任务栏出现程序图标。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAY = 0x8000;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private readonly HwndSource _source;
    private Icon _icon;
    private NOTIFYICONDATA _data;
    private bool _disposed;

    /// <summary>左键单击。</summary>
    public event Action? LeftClicked;

    /// <summary>右键单击。</summary>
    public event Action? RightClicked;

    public TrayIcon(IntPtr hwnd, Icon icon)
    {
        _icon = icon;
        _source = HwndSource.FromHwnd(hwnd) ??
                  throw new InvalidOperationException("无法获取宿主窗口的 HwndSource");
        _source.AddHook(WndProc);

        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon.Handle,
            szTip = "音频设备切换器",
        };
        Shell_NotifyIcon(NIM_ADD, ref _data);
    }

    /// <summary>更新悬停提示文本。</summary>
    public void UpdateTip(string tip)
    {
        if (_disposed) return;
        _data.szTip = tip.Length > 127 ? tip[..127] : tip;
        _data.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
    }

    /// <summary>替换托盘图标（主题切换时变色）。</summary>
    public void UpdateIcon(Icon icon)
    {
        if (_disposed) return;
        var old = _icon;
        _icon = icon;
        _data.hIcon = icon.Handle;
        _data.uFlags = NIF_ICON;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
        old.Dispose();
    }

    /// <summary>气泡通知。</summary>
    public void ShowBalloon(string title, string text)
    {
        if (_disposed) return;
        _data.szInfoTitle = title;
        _data.szInfo = text;
        _data.dwInfoFlags = NIIF_INFO;
        _data.uFlags = NIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == WM_TRAY)
        {
            switch ((uint)lParam)
            {
                case WM_LBUTTONUP: LeftClicked?.Invoke(); break;
                case WM_RBUTTONUP: RightClicked?.Invoke(); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIcon(NIM_DELETE, ref _data);
        _source.RemoveHook(WndProc);
        _icon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);
}
