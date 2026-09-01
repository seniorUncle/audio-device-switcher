using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioSwitcher.Audio;
using AudioSwitcher.Services;
using MsgBox = System.Windows.MessageBox;

namespace AudioSwitcher;

// ============================================================
// 用途：主设置窗口逻辑——设备列表展示、切换/启用禁用设备、
// 开机自启、标题栏交互与设备列表展开收起动画。
// ============================================================
public partial class MainWindow : Window
{
    private readonly AudioDeviceManager _audio;
    private readonly TrayIconService _tray;

    public MainWindow(AudioDeviceManager audio, TrayIconService tray)
    {
        InitializeComponent();
        _audio = audio;
        _tray = tray;
        Backdrop.ApplyMainWindow(this);
        using (var icon = IconFactory.Create(32))
        {
            Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        _tray.DeviceSwitched += OnDeviceSwitched;
        Loaded += (_, _) => Refresh();

        UpdateWindowsIcon();
        ThemeService.ThemeChanged += UpdateWindowsIcon;
    }

    /// <summary>按当前主题刷新标题栏的 Windows 图标颜色（深色用白、浅色用黑）。</summary>
    private void UpdateWindowsIcon()
    {
        Dispatcher.Invoke(() =>
        {
            if (WindowsIcon is not null)
                WindowsIcon.Source = IconFactory.LoadMenuIcon("AudioSwitcher.Assets.windows-fill.png", ThemeService.IsDarkTheme());
        });
    }

    /// <summary>跳转到 Windows 的"更多声音设置"（经典声音控制面板）。</summary>
    private void OpenWindowsSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "mmsys.cpl",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MsgBox.Show($"打开系统声音设置失败：{ex.Message}", "音频设备切换器",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDeviceSwitched() => Dispatcher.Invoke(RefreshIfVisible);

    private void RefreshIfVisible()
    {
        if (IsVisible) Refresh();
    }

    private void Refresh()
    {
        try
        {
            // 输出设备：拆分为已启用/未启用两区
            var outputs = _audio.GetOutputDevicesIncludingDisabled();
            var disabledOutputs = outputs.Where(d => !d.IsActive).ToList();
            DeviceList.ItemsSource = outputs.Where(d => d.IsActive).ToList();
            DisabledDeviceList.ItemsSource = disabledOutputs;
            // 无未启用设备时隐藏标题并强制收起列表；有设备时列表可见性由折叠状态控制
            bool hasDisabledOut = disabledOutputs.Count > 0;
            DisabledOutputHeader.Visibility = hasDisabledOut ? Visibility.Visible : Visibility.Collapsed;
            if (!hasDisabledOut) DisabledDeviceList.Visibility = Visibility.Collapsed;
            // 列表枚举时已内置默认设备标记，直接取用，避免再次查询默认设备
            CurrentDeviceText.Text = outputs.FirstOrDefault(d => d.IsDefault)?.Name ?? "无可用设备";

            // 输入设备：拆分为已启用/未启用两区
            var inputs = _audio.GetInputDevicesIncludingDisabled();
            var disabledInputs = inputs.Where(d => !d.IsActive).ToList();
            InputDeviceList.ItemsSource = inputs.Where(d => d.IsActive).ToList();
            DisabledInputDeviceList.ItemsSource = disabledInputs;
            bool hasDisabledIn = disabledInputs.Count > 0;
            DisabledInputHeader.Visibility = hasDisabledIn ? Visibility.Visible : Visibility.Collapsed;
            if (!hasDisabledIn) DisabledInputDeviceList.Visibility = Visibility.Collapsed;
            CurrentInputText.Text = inputs.FirstOrDefault(d => d.IsDefault)?.Name ?? "无可用设备";
        }
        catch (Exception ex)
        {
            CurrentDeviceText.Text = "读取失败";
            MsgBox.Show($"读取音频设备失败：{ex.Message}", "音频设备切换器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        AutoStartToggle.IsChecked = AutoStart.IsEnabled();
    }

    private void DeviceItem_Click(object sender, RoutedEventArgs e)
    {
        // 仅已启用设备可设为默认；禁用设备卡片点击无操作（避免误切报错）
        if (sender is FrameworkElement { DataContext: AudioDevice { IsActive: true }, Tag: string id })
        {
            try
            {
                _audio.SetDefaultDevice(id);
            }
            catch (Exception ex)
            {
                MsgBox.Show($"切换设备失败：{ex.Message}", "音频设备切换器",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Refresh();
            _tray.Refresh();
        }
    }

    /// <summary>设备开关胶囊：启用/禁用对应设备，操作后刷新分区列表。</summary>
    private void DeviceToggle_Click(object sender, RoutedEventArgs e)
    {
        // 开关位于卡片内部，阻止点击事件冒泡到外层卡片，避免同时触发"设为默认"
        e.Handled = true;
        if (sender is ToggleButton { Tag: string id } toggle)
        {
            bool enable = toggle.IsChecked == true;
            try
            {
                _audio.SetDeviceEnabled(id, enable);
            }
            catch (Exception ex)
            {
                MsgBox.Show($"{(enable ? "启用" : "禁用")}设备失败：{ex.Message}", "音频设备切换器",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Refresh();
            _tray.Refresh();
        }
    }

    /// <summary>分区标题点击：按标题开关对应的设备列表（展开/收起），并播放上下滑入滑出动画。</summary>
    private void SectionHeader_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Name: var name }) return;
        // XAML 初次加载时 IsChecked 初始赋值也会触发本事件，此时命名列表控件尚未创建，需判空
        if (DeviceList == null && DisabledDeviceList == null && InputDeviceList == null && DisabledInputDeviceList == null)
            return;
        bool expand = ((ToggleButton)sender).IsChecked == true;
        switch (name)
        {
            case "OutputEnabledHeader": AnimateVisibility(DeviceList!, expand); break;
            case "DisabledOutputHeader": AnimateVisibility(DisabledDeviceList!, expand); break;
            case "InputEnabledHeader": AnimateVisibility(InputDeviceList!, expand); break;
            case "DisabledInputHeader": AnimateVisibility(DisabledInputDeviceList!, expand); break;
        }
    }

    /// <summary>设备列表展开/收起动画：展开时从顶部淡入下滑，收起时上滑淡出。</summary>
    private void AnimateVisibility(UIElement target, bool expand)
    {
        if (target == null) return;
        var trans = new TranslateTransform();
        target.RenderTransform = trans;
        target.RenderTransformOrigin = new Point(0.5, 0);
        if (expand)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            target.Opacity = 0;
            target.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            trans.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-28, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            target.Visibility = Visibility.Visible;
        }
        else
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
            fade.Completed += (_, _) => target.Visibility = Visibility.Collapsed;
            target.BeginAnimation(UIElement.OpacityProperty, fade);
            trans.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -26, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        }
    }

    private void AutoStartToggle_Changed(object sender, RoutedEventArgs e)
    {
        // 程序启动时的初始化赋值也会触发，此时静默更新即可
        if (!IsLoaded) return;
        try
        {
            AutoStart.SetEnabled(AutoStartToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            MsgBox.Show($"设置开机自启失败：{ex.Message}", "音频设备切换器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // 关闭即隐藏到托盘，保持常驻以便快捷切换
        Hide();
    }
}
