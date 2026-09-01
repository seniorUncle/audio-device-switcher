using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioSwitcher.Services;

/// <summary>
/// 同步 Windows 主题：
/// - 跟随系统深浅色模式（背景/卡片/文字/边框整体切换）；
/// - 使用系统强调色作为界面高亮色（Primary / Accent）。
/// 通过 {DynamicResource} 引用资源键，运行时替换 Brush 即可即时生效。
/// </summary>
public static class ThemeService
{
    // 深色调色板
    private static readonly Color DarkBg = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color DarkCard = Color.FromRgb(0x2B, 0x2F, 0x36);
    private static readonly Color DarkCardHover = Color.FromRgb(0x33, 0x39, 0x44);
    private static readonly Color DarkText = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color DarkTextSecondary = Color.FromRgb(0x9A, 0xA3, 0xB0);
    private static readonly Color DarkBorder = Color.FromRgb(0x3A, 0x3F, 0x47);

    // 浅色调色板
    private static readonly Color LightBg = Color.FromRgb(0xF3, 0xF4, 0xF6);
    private static readonly Color LightCard = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color LightCardHover = Color.FromRgb(0xED, 0xEE, 0xF0);
    private static readonly Color LightText = Color.FromRgb(0x1B, 0x1B, 0x1E);
    private static readonly Color LightTextSecondary = Color.FromRgb(0x62, 0x66, 0x6B);
    private static readonly Color LightBorder = Color.FromRgb(0xDD, 0xDF, 0xE3);

    private const string KeyCard = "CardBrush";
    private const string KeyCardHover = "CardHoverBrush";
    private const string KeyText = "TextPrimary";
    private const string KeyTextSecondary = "TextSecondary";
    private const string KeyBorder = "BorderBrush";
    private const string KeyAccent = "AccentBrush";
    private const string KeySurface = "SurfaceBrush";
    private const string KeyTabSelected = "TabSelectedBrush";
    private const string KeySelectedText = "SelectedTextBrush";

    /// <summary>初始化：立即应用一次当前主题，并订阅系统主题变化事件。</summary>
    public static void Init()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>主题（深浅色、强调色）变化后触发，用于刷新托盘图标等非 XAML 资源。</summary>
    public static event Action? ThemeChanged;

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // 深浅色、强调色等主题相关变化均会触发；回到 UI 线程统一刷新
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Apply();
            ThemeChanged?.Invoke();
        });
    }

    /// <summary>按当前系统主题刷新应用资源。<b>须在 UI 线程调用。</b></summary>
    public static void Apply()
    {
        if (Application.Current == null) return;

        bool dark = IsDarkTheme();
        Color accent = GetSystemAccent();

        var res = Application.Current.Resources;
        res[KeyCard] = NewBrush(dark ? DarkCard : LightCard);
        res[KeyCardHover] = NewBrush(dark ? DarkCardHover : LightCardHover);
        res[KeyText] = NewBrush(dark ? DarkText : LightText);
        res[KeyTextSecondary] = NewBrush(dark ? DarkTextSecondary : LightTextSecondary);
        res[KeyBorder] = NewBrush(dark ? DarkBorder : LightBorder);
        // 表面背景：系统透明效果开启时使用半透明（配合亚克力模糊），关闭时使用不透明底色
        bool transparent = Backdrop.IsTransparencyEnabled();
        Color surfaceBg = dark ? DarkBg : LightBg;
        if (transparent) surfaceBg = Color.FromArgb(dark ? (byte)0xE0 : (byte)0xE8, surfaceBg.R, surfaceBg.G, surfaceBg.B);
        res[KeySurface] = NewBrush(surfaceBg);
        // 强调色：作为文字/勾选时，保证在对应背景上有足够对比度（深色提亮、浅色压深）
        res[KeyAccent] = NewBrush(AdjustAccentForText(accent, dark));
        // 标签选中：固定蓝色（文字随主题调亮/压深保证可读，背景为半透明蓝药丸）
        res[KeyTabSelected] = NewBrush(Color.FromArgb(0x33, 0x00, 0x73, 0xE6));
        res[KeySelectedText] = NewBrush(AdjustAccentForText(Color.FromRgb(0x00, 0x73, 0xE6), dark));
    }

    /// <summary>适配强调色作文字/图标用：深色模式提亮、浅色模式压深，保证可读。</summary>
    private static Color AdjustAccentForText(Color c, bool dark)
    {
        RgbToHsl(c, out double h, out double s, out double l);
        l = dark ? Math.Max(l, 0.60) : Math.Min(l, 0.45);
        return HslToRgb(h, s, l);
    }

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 1e-9)
        {
            h = s = 0;
            return;
        }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6.0;
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (Math.Abs(s) < 1e-9)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }
        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    private static SolidColorBrush NewBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>是否处于 Windows 深色模式（AppsUseLightTheme = 0）。</summary>
    public static bool IsDarkTheme()
    {
        try
        {
            var v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return v is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>读取系统强调色（虚实色）。失败时回退到默认 Windows 蓝。</summary>
    private static Color GetSystemAccent()
    {
        try
        {
            // 优先读取 Explorer\Accent 下的实色强调色（AccentColorMenu）
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key != null)
            {
                foreach (var name in key.GetSubKeyNames())
                {
                    if (!name.StartsWith("Accent ", StringComparison.OrdinalIgnoreCase)) continue;
                    using var sub = key.OpenSubKey(name);
                    if (sub?.GetValue("AccentColorMenu") is int c)
                        return Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
                }
            }

            // 回退到 DWM 颜色化色（同为 ABGR 存储，低字节为 R）
            var cw = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "ColorizationColor", null);
            if (cw is int cc)
                return Color.FromRgb((byte)(cc & 0xFF), (byte)((cc >> 8) & 0xFF), (byte)((cc >> 16) & 0xFF));
        }
        catch
        {
            // 忽略，走默认值
        }

        return Color.FromRgb(0x00, 0x73, 0xE6); // 默认蓝色 #0073E6
    }
}