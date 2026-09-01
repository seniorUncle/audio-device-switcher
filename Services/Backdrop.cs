using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace AudioSwitcher.Services;

/// <summary>
/// Windows 亚克力/毛玻璃背景。
/// - 跟随系统“透明效果”开关（EnableTransparency）；
/// - 深色/浅色下使用不同的半透明着色；
/// - 兼容 Win10（SetWindowCompositionAttribute）与 Win11（含圆角）。
/// </summary>
public static class Backdrop
{
    private const int WCA_ACCENT_POLICY = 19;
    private const int AccentAcrylicBlurBehind = 4;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;        // 圆角（Win11）
    private const int DWMSBT_TRANSIENTWINDOW = 3; // 亚克力

    /// <summary>系统是否开启“透明效果”。</summary>
    public static bool IsTransparencyEnabled()
    {
        try
        {
            var v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency", 1);
            return v is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>为主窗口启用亚克力系统背景与圆角（需在窗口句柄可用后调用）。</summary>
    public static void ApplyMainWindow(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !IsTransparencyEnabled()) return;

            // Win11 DWM 系统背景：由 DWM 提供亚克力模糊并自动圆角裁切，避免四角露底
            int type = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        };
    }

    /// <summary>为托盘菜单等弹出层窗口启用亚克力背景与圆角。</summary>
    public static void ApplyPopup(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsTransparencyEnabled()) return;

        int tint = ThemeService.IsDarkTheme() ? unchecked((int)0x99000000) : unchecked((int)0x88FFFFFF);
        var accent = new AccentPolicy { AccentState = AccentAcrylicBlurBehind, GradientColor = tint };
        int len = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                DataLength = len,
                Data = ptr,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public int DataLength;
        public IntPtr Data;
    }
}