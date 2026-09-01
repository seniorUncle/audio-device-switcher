using System.IO;
using System.Windows;
using AudioSwitcher.Audio;
using AudioSwitcher.Services;
using WinApp = System.Windows.Application;
using MsgBox = System.Windows.MessageBox;

namespace AudioSwitcher;

// ============================================================
// 用途：应用入口——单实例互斥、托盘服务初始化、设置窗口单例管理与异常日志。
// ============================================================
public partial class App : WinApp
{
    private const string MutexName = @"Local\AudioSwitcher_SingleInstance";

    private Mutex? _mutex;
    private AudioDeviceManager? _audio;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            StartupCore();
        }
        catch (Exception ex)
        {
            LogAndShowError("startup-error.log", $"启动失败：{ex.Message}", ex);
            Shutdown();
            return;
        }
    }

    private void StartupCore()
    {
        ThemeService.Init();

        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MsgBox.Show("音频设备切换器已在运行，请查看系统托盘图标。", "音频设备切换器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _audio = new AudioDeviceManager();
        _tray = new TrayIconService(_audio, OpenSettings);

        if (!AutoStart.IsIntroShown())
        {
            AutoStart.SetIntroShown();
            _tray.ShowIntro();
        }
    }

    /// <summary>从托盘打开设置窗口（单例）。</summary>
    private void OpenSettings()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (MainWindow is not MainWindow win)
                {
                    win = new MainWindow(_audio!, _tray!);
                    MainWindow = win;
                    win.Closed += (_, _) => MainWindow = null;
                }

                win.Show();
                if (win.WindowState == WindowState.Minimized)
                    win.WindowState = WindowState.Normal;
                win.Activate();
            }
            catch (Exception ex)
            {
                LogAndShowError("win-open-error.log", $"打开设置窗口失败：{ex.Message}", ex);
            }
        });
    }

    /// <summary>将异常写入应用目录日志文件并弹窗提示异常信息。</summary>
    private static void LogAndShowError(string logFileName, string message, Exception ex)
    {
        var log = Path.Combine(AppContext.BaseDirectory, logFileName);
        File.WriteAllText(log, ex.ToString());
        MsgBox.Show($"{message}\n详情见 {log}", "音频设备切换器",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _audio?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
