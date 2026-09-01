using Microsoft.Win32;

// ============================================================
// 用途：开机自启（注册表 Run 键）与首启提示标记（HKCU\Software\AudioSwitcher）管理。
// ============================================================
namespace AudioSwitcher.Services;

/// <summary>
/// 开机自启（注册表 Run 键）与首启提示标记（HKCU\Software\AudioSwitcher）。
/// 轻量实现，不依赖计划任务。
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AudioSwitcher";

    private const string AppKeyPath = @"Software\AudioSwitcher";
    private const string IntroShownValue = "IntroShown";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string value && value.Length > 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key == null) throw new InvalidOperationException("无法访问注册表 Run 键");
        if (enabled)
        {
            string exe = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;
            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("无法获取程序路径");
            key.SetValue(RunValueName, $"\"{exe}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsIntroShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AppKeyPath);
        return key?.GetValue(IntroShownValue) is int v && v != 0;
    }

    public static void SetIntroShown()
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppKeyPath);
        key?.SetValue(IntroShownValue, 1, RegistryValueKind.DWord);
    }
}
