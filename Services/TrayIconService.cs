using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AudioSwitcher.Audio;

namespace AudioSwitcher.Services;

/// <summary>
/// 系统托盘服务：原生 Shell_NotifyIcon + WPF ContextMenu。
/// - 隐藏宿主窗口承载托盘消息，不出现在任务栏；
/// - 左键/右键单击托盘图标弹出深色菜单；
/// - 菜单弹出在鼠标上方一点（水平对齐鼠标，越界自动钳制）；
/// - 延迟打开 + 全局低级鼠标钩子，确保点击菜单外区域自动收起。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    private readonly AudioDeviceManager _audio;
    private readonly Action _openSettings;
    private readonly Window _host;
    private readonly TrayIcon _tray;
    private ContextMenu? _menu;
    private IntPtr _mouseHook;
    private HookProc? _hookProc;

    /// <summary>音量滑块代码赋值时置位，避免触发 ValueChanged 回写造成循环。</summary>
    private bool _suppressVolume;

    /// <summary>切换设备后触发（用于通知打开的窗口刷新）。</summary>
    public event Action? DeviceSwitched;

    public TrayIconService(AudioDeviceManager audio, Action openSettings)
    {
        _audio = audio;
        _openSettings = openSettings;

        // 隐藏宿主窗口：仅用于接收托盘回调消息与承载菜单定位，不出现在任务栏
        _host = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
        };
        _host.Show();

        _tray = new TrayIcon(new WindowInteropHelper(_host).Handle, IconFactory.Create(16, ThemeService.IsDarkTheme()));
        _tray.LeftClicked += ShowDeviceMenu;
        _tray.RightClicked += ShowSettingsMenu;
        ThemeService.ThemeChanged += UpdateIconTheme;
        Refresh();
    }

    /// <summary>随系统主题刷新托盘图标颜色（深色用白、浅色用黑）。</summary>
    private void UpdateIconTheme()
    {
        _tray.UpdateIcon(IconFactory.Create(16, ThemeService.IsDarkTheme()));
    }

    /// <summary>刷新悬停提示（同时显示当前输出与输入设备，设备可能发生变化）。</summary>
    public void Refresh()
    {
        var output = _audio.GetDefaultDevice();
        var input = _audio.GetDefaultInputDevice();
        string tip = (output == null && input == null)
            ? "音频设备切换器"
            : Truncate($"当前输出：{output?.Name ?? "无"}\n当前输入：{input?.Name ?? "无"}", 127);
        _tray.UpdateTip(tip);
    }

    /// <summary>左键单击：弹出设备选择菜单（延迟打开，让 WPF Popup 能正确获得鼠标捕获）。</summary>
    private void ShowDeviceMenu()
    {
        _host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ShowMenu(BuildDeviceMenu()));
    }

    /// <summary>右键单击：弹出设置/退出菜单。</summary>
    private void ShowSettingsMenu()
    {
        _host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ShowMenu(BuildSettingsMenu()));
    }

    /// <summary>
    /// 弹出菜单。默认按光标定位（底部距鼠标 6px，整体左移 25px，越界自动钳制）；
    /// 传入 fixedX/fixedBottomY 时（切换标签重建）保持菜单底部贴齐原位、高度向上扩展，避免下方留白与跳动。
    /// </summary>
    private void ShowMenu(ContextMenu menu, double? fixedX = null, double? fixedBottomY = null)
    {
        CloseMenu();
        _menu = menu;

        // 先测量尺寸，便于定位
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = menu.DesiredSize.Width;
        double h = menu.DesiredSize.Height;

        var wa = SystemParameters.WorkArea;
        double x, y;
        if (fixedX.HasValue && fixedBottomY.HasValue)
        {
            // 切换标签：锚定底部，顶部 = 底部 - 新高度（仅钳制到工作区内）
            x = Math.Round(Math.Clamp(fixedX.Value, wa.Left, Math.Max(wa.Left, wa.Right - w)));
            y = Math.Round(Math.Clamp(fixedBottomY.Value - h, wa.Top, Math.Max(wa.Top, wa.Bottom - h)));
        }
        else
        {
            // 光标位置（物理像素）换算为 DIP，菜单底部位于鼠标上方一点
            GetCursorPos(out var pt);
            double scale = VisualTreeHelper.GetDpi(_host).PixelsPerDip;
            double cx = pt.x / scale;
            double cy = pt.y / scale;

            // 菜单底部在鼠标上方 baseGap 距离，整体左移 leftShift；坐标取整到整像素避免边框被裁
            const double baseGap = 6;
            const double leftShift = 25;
            x = Math.Round(Math.Clamp(cx - leftShift, wa.Left, Math.Max(wa.Left, wa.Right - w)));
            y = Math.Round(Math.Clamp(cy - h - baseGap, wa.Top, Math.Max(wa.Top, wa.Bottom - h)));
        }

        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = x;
        menu.VerticalOffset = y;
        menu.IsOpen = true;

        // 为菜单弹出窗口启用亚克力背景（跟随系统透明效果）
        if (PresentationSource.FromVisual(menu) is HwndSource src)
            Backdrop.ApplyPopup(src.Handle);

        InstallMouseHook();
    }

    private void CloseMenu()
    {
        if (_menu != null)
        {
            _menu.IsOpen = false;
            _menu = null;
        }
    }

    // ---- 全局低级鼠标钩子：保证点击菜单外区域时自动收起 ----

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _hookProc = MouseHookProc;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (!IsPointInMenu(info.pt))
            {
                CloseMenu();
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool IsPointInMenu(POINT pt)
    {
        if (_menu is not { IsOpen: true }) return true; // 无打开的菜单时忽略
        var source = PresentationSource.FromVisual(_menu) as HwndSource;
        if (source == null) return true;
        GetWindowRect(source.Handle, out var r);
        return pt.x >= r.Left && pt.x <= r.Right && pt.y >= r.Top && pt.y <= r.Bottom;
    }

    /// <summary>设备列表标签（决定标题行下方显示输出还是输入设备）。</summary>
    private enum DeviceTab { Output, Input }

    /// <summary>当前选中的设备标签。</summary>
    private DeviceTab _deviceTab = DeviceTab.Output;

    /// <summary>将 App 级细滚动条样式注入到菜单自身资源，确保弹窗内滚动条（内层设备列表/外层菜单）均生效。</summary>
    private static void InjectThinScrollBar(ContextMenu menu)
    {
        if (Application.Current.FindResource("ThinScrollBar") is Style thin)
            menu.Resources[typeof(ScrollBar)] = thin;
    }

    /// <summary>
    /// 设备选择菜单：标题行「输出设备 / 输入设备」标签在同一行，
    /// 下方仅显示当前标签对应的设备列表，点击标题即切换列表。
    /// 传入已打开的菜单时复用其弹窗窗口原地重建内容，避免关闭→重开造成的闪烁。
    /// </summary>
    private ContextMenu BuildDeviceMenu(ContextMenu? menu = null)
    {
        menu ??= new ContextMenu();
        menu.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        // 弹窗内滚动条可能不继承 App 级隐式样式，注入细滚动条样式以确保生效
        InjectThinScrollBar(menu);
        menu.Items.Clear();

        var outputDevices = _audio.GetOutputDevices();
        var inputDevices = _audio.GetInputDevices();
        // 输入/输出切换时宽度保持一致：以两列表中最长项所需宽度作为菜单最小宽度，同时保证音量区滑块有足够宽度
        double widest = Math.Max(MeasureWidestDeviceName(outputDevices), MeasureWidestDeviceName(inputDevices));
        menu.MinWidth = Math.Max(widest, 280);

        // 音量调节区（输出/输入滑块）：位于菜单最上方
        AddVolumeSection(menu);

        // 标题行：两个可切换标签各占一半宽度（选中的高亮）
        var header = new MenuItem
        {
            Focusable = false,
            IsTabStop = false,
            Template = SimpleContentTemplate(typeof(MenuItem), "Header"),
        };
        var tabPanel = new Grid();
        tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var outputTab = MakeTabButton("输出设备", DeviceTab.Output);
        Grid.SetColumn(outputTab, 0);
        var inputTab = MakeTabButton("输入设备", DeviceTab.Input);
        Grid.SetColumn(inputTab, 1);
        tabPanel.Children.Add(outputTab);
        tabPanel.Children.Add(inputTab);
        header.Header = tabPanel;
        menu.Items.Add(header);

        // 当前标签对应的设备列表
        var (devices, emptyText) = _deviceTab == DeviceTab.Output
            ? (outputDevices, "未检测到可用输出设备")
            : (inputDevices, "未检测到可用输入设备");
        // 以输出设备列表高度为固定高度：切换输入/输出标签时菜单高度保持不变
        AddDevices(menu, devices, emptyText, MeasureDeviceListHeight(outputDevices));
        return menu;
    }

    /// <summary>在菜单顶部添加输出/输入音量调节区（两个滑块）。</summary>
    private void AddVolumeSection(ContextMenu menu)
    {
        // 音量区上下加大留白：首行顶部、末行底部外边距增大，两行之间留 6px
        var outRow = MakeVolumeRow(EDataFlow.Render);
        outRow.Margin = new Thickness(4, 15, 4, 3);
        menu.Items.Add(outRow);
        var inRow = MakeVolumeRow(EDataFlow.Capture);
        inRow.Margin = new Thickness(4, 3, 4, 15);
        menu.Items.Add(inRow);
    }

    /// <summary>
    /// 创建一行音量滑块：图标 + 滑块 + 百分比。滑块控制当前默认设备的主音量；
    /// 图标随音量等级切换（输出：0% 静音 / 1%-50% / 51%-100% 三档，
    /// 输入：0% 静音 / 1%-100% 两档），静音时固定显示静音图标；
    /// 点击图标切换静音（静音不改变音量值，取消静音即恢复原音量）。
    /// 用无边框模板并保持菜单开启，避免拖拽/点击滑块时菜单收起。
    /// </summary>
    private MenuItem MakeVolumeRow(EDataFlow flow)
    {
        var icon = new Image
        {
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.SetResourceReference(FrameworkElement.StyleProperty, "VolumeSlider");

        var percent = new TextBlock
        {
            FontSize = 12,
            MinWidth = 36,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        percent.SetResourceReference(Control.ForegroundProperty, "TextPrimary");

        // 布局：左缩进对齐设备列表（菜单项外边距 4 + 内边距 12 + 固定勾选列 20），右侧预留外边距
        // 透明背景让整行都可命中，滚轮在行内任意位置（图标/空白/滑块）都能调节
        var grid = new Grid { Margin = new Thickness(32, 0, 12, 0), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(icon);
        grid.Children.Add(slider);
        grid.Children.Add(percent);

        // 初始值：读取当前默认设备音量与静音状态
        float? volume = _audio.GetVolume(flow);
        bool? mute = _audio.GetMute(flow);
        double initial = 0;
        if (volume.HasValue)
        {
            _suppressVolume = true;
            slider.Value = Math.Round(volume.Value * 100);
            _suppressVolume = false;
            percent.Text = $"{slider.Value:0}%";
            initial = slider.Value;
        }
        else
        {
            slider.IsEnabled = false;
            percent.Text = "--";
        }
        UpdateVolumeIcon(icon, flow, initial, mute ?? false);

        // 点击图标切换静音/恢复原音量
        icon.MouseLeftButtonUp += (_, _) => ToggleMute(icon, flow);

        // 拖动/点击滑块时实时设置音量，同时取消静音
        slider.ValueChanged += (_, e) =>
        {
            if (_suppressVolume) return;
            double v = Math.Round(e.NewValue);
            percent.Text = $"{v:0}%";
            if (v > 0) { try { _audio.SetMute(flow, false); } catch { } }
            UpdateVolumeIcon(icon, flow, v, false);
            try { _audio.SetVolume(flow, (float)(v / 100.0)); }
            catch { }
        };

        // 滚轮调节：整行任意位置滚动一格 ±2%，扩大鼠标感应范围，赋值触发 ValueChanged 同步
        grid.MouseWheel += (_, e) =>
        {
            e.Handled = true;
            if (_suppressVolume || !slider.IsEnabled) return;
            slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 2 : -2),
                slider.Minimum, slider.Maximum);
        };

        return new MenuItem
        {
            Header = grid,
            StaysOpenOnClick = true,
            Focusable = false,
            IsTabStop = false,
            Margin = new Thickness(4, 1, 4, 1),
            Padding = new Thickness(0),
            Template = SimpleContentTemplate(typeof(MenuItem), "Header"),
        };
    }

    /// <summary>根据静音状态与音量等级切换对应图标（按当前主题着色）。</summary>
    private void UpdateVolumeIcon(Image icon, EDataFlow flow, double volumePercent, bool muted)
    {
        string resource;
        if (muted || volumePercent <= 0)
            resource = flow == EDataFlow.Capture ? "AudioSwitcher.Assets.microphone-off.png"
                                                 : "AudioSwitcher.Assets.volume-3.png";
        else if (flow == EDataFlow.Capture)
            resource = "AudioSwitcher.Assets.microphone.png";
        else if (volumePercent <= 50)
            resource = "AudioSwitcher.Assets.volume-2.png";
        else
            resource = "AudioSwitcher.Assets.volume.png";
        icon.Source = IconFactory.LoadMenuIcon(resource, ThemeService.IsDarkTheme());
    }

    /// <summary>切换默认设备静音状态：静音不改变音量值，取消静音即恢复原音量。</summary>
    private void ToggleMute(Image icon, EDataFlow flow)
    {
        bool nowMuted = !(_audio.GetMute(flow) ?? false);
        try { _audio.SetMute(flow, nowMuted); }
        catch { return; }
        float? level = _audio.GetVolume(flow);
        UpdateVolumeIcon(icon, flow, (level ?? 0f) * 100, nowMuted);
    }

    /// <summary>测量设备列表中最长项所需宽度（含固定勾选列、菜单项内边距/外边距与菜单边框），用于保证输入/输出列表宽度一致。</summary>
    private double MeasureWidestDeviceName(IReadOnlyList<AudioDevice> devices)
    {
        double max = 0;
        var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal,
            FontWeights.Normal, FontStretches.Normal);
        double pxPerDip = VisualTreeHelper.GetDpi(_host).PixelsPerDip;
        foreach (var d in devices)
        {
            var ft = new FormattedText(d.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, 12, Brushes.Black, pxPerDip);
            max = Math.Max(max, ft.WidthIncludingTrailingWhitespace);
        }
        // 固定勾选列(20) + 菜单项 Padding(12*2) + Margin(4*2) + 菜单边框(1*2)
        return max + 20 + 24 + 8 + 2;
    }

    /// <summary>创建一个标签按钮：选中时带药丸形高亮背景 + 强调色加粗文字，点击后切换标签并重建菜单。</summary>
    private Button MakeTabButton(string text, DeviceTab tab)
    {
        bool selected = _deviceTab == tab;
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(10, 9, 10, 9),
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = TabButtonTemplate(),
        };
        // 标题文字统一为白色，选中态用药丸形高亮背景 + 加粗区分
        btn.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        if (selected)
            btn.SetResourceReference(Control.BackgroundProperty, "TabSelectedBrush");
        else
            btn.Background = Brushes.Transparent;
        btn.Click += (_, _) =>
        {
            if (_deviceTab == tab) return; // 已选中无需切换
            _deviceTab = tab;
            // 复用已打开的菜单弹窗窗口原地重建内容，避免关闭→重开造成的闪烁；
            // 重建后强制弹窗根同步布局，直接用窗口实际矩形（物理像素转 DIP）
            // 定位，保证底部精确贴齐原位、消除测量误差导致的轻微跳动
            if (_menu is { IsOpen: true } old && PresentationSource.FromVisual(old) is HwndSource src)
            {
                double scale = VisualTreeHelper.GetDpi(old).PixelsPerDip;
                GetWindowRect(src.Handle, out var r0);
                double left = r0.Left / scale;
                double bottom = r0.Bottom / scale;

                BuildDeviceMenu(old);
                // 同步更新弹窗根（含 PopupRoot 窗口尺寸），使窗口立即跟随新内容
                (src.RootVisual as FrameworkElement)?.UpdateLayout();

                GetWindowRect(src.Handle, out var r1);
                double w = (r1.Right - r1.Left) / scale;
                double h = (r1.Bottom - r1.Top) / scale;
                var wa = SystemParameters.WorkArea;
                old.HorizontalOffset = Math.Round(Math.Clamp(left, wa.Left, Math.Max(wa.Left, wa.Right - w)));
                old.VerticalOffset = Math.Round(Math.Clamp(bottom - h, wa.Top, Math.Max(wa.Top, wa.Bottom - h)));
            }
            else
            {
                ShowMenu(BuildDeviceMenu());
            }
        };
        return btn;
    }

    // 菜单模板为不可变对象，可跨实例复用，避免每次开菜单都重建 FrameworkElementFactory。
    private static readonly ControlTemplate TabButtonTemplateInstance = BuildTabButtonTemplate();
    private static readonly ControlTemplate SimpleHeaderTemplateInstance =
        BuildSimpleContentTemplate(typeof(MenuItem), "Header");

    /// <summary>标签按钮模板：圆角边框（背景随按钮 Background）包裹居中文本，实现药丸形选中效果。</summary>
    private static ControlTemplate TabButtonTemplate() => TabButtonTemplateInstance;

    private static ControlTemplate BuildTabButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,
            new Binding { RelativeSource = RelativeSource.TemplatedParent, Path = new PropertyPath(Control.BackgroundProperty) });
        border.SetBinding(Border.PaddingProperty,
            new Binding { RelativeSource = RelativeSource.TemplatedParent, Path = new PropertyPath(Control.PaddingProperty) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    /// <summary>
    /// 添加设备列表（当前设备带勾选），无设备时显示空提示；
    /// 列表区高度固定为 fixedHeight（以输出设备列表高度为基准），切换输入/输出标签时菜单高度保持不变；
    /// 超出固定高度的设备滚动到下方选择。
    /// </summary>
    private void AddDevices(ContextMenu menu, IReadOnlyList<AudioDevice> devices, string emptyText, double fixedHeight)
    {
        // 统一设备行高：先测量单行文本高度作为固定行高，再应用到所有设备项，
        // 保证输入/输出列表中每一行等高，避免切换列表时菜单高度不一致
        var probe = new MenuItem { Header = "测量行高" };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double fixedRowHeight = probe.DesiredSize.Height;

        // 构建设备菜单项
        var items = new List<MenuItem>(Math.Max(devices.Count, 1));
        if (devices.Count == 0)
        {
            var empty = new MenuItem { Header = emptyText, IsEnabled = false, Height = fixedRowHeight };
            empty.SetResourceReference(Control.ForegroundProperty, "TextSecondary");
            // 保持与设备行一致的勾选列缩进
            empty.SetResourceReference(FrameworkElement.StyleProperty, "DeviceMenuItem");
            items.Add(empty);
        }
        else
        {
            foreach (var d in devices)
            {
                var mi = new MenuItem
                {
                    Header = d.Name,
                    IsChecked = d.IsDefault,
                    Height = fixedRowHeight,
                    // 切换设备后保持菜单开启（内联项默认点击会收起菜单，需显式保持打开）
                    StaysOpenOnClick = true,
                };
                // 显式绑定主题前景色（浅色模式下弹窗内隐式样式级联不可靠）
                mi.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
                // 仅设备列表项使用带勾选列的样式
                mi.SetResourceReference(FrameworkElement.StyleProperty, "DeviceMenuItem");
                var id = d.Id;
                mi.Tag = id;
                mi.Click += (_, _) =>
                {
                    SwitchDevice(id);
                    // 菜单保持开启：就地更新勾选状态到新选中的设备
                    foreach (var it in items)
                        it.IsChecked = Equals(it.Tag, id);
                };
                items.Add(mi);
            }
        }

        // 所有列表统一放入固定高度的滚动容器：切换标签高度不变，超出部分滚动查看
        var panel = new StackPanel();
        foreach (var it in items) panel.Children.Add(it);

        var scroll = new ScrollViewer
        {
            Content = panel,
            // 固定为输出设备列表高度；设备少时下方留白，设备多时在固定高度内滚动
            Height = fixedHeight,
            // 隐藏滚动条，保留滚轮滚动
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };

        var wrapper = new MenuItem
        {
            Header = scroll,
            StaysOpenOnClick = true,
            Focusable = false,
            IsTabStop = false,
            // 外边距置 0：间距完全由内部设备项自身的 Margin 提供，
            // 避免与直接平铺列表相比左右/下方多出一份边距
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Template = SimpleContentTemplate(typeof(MenuItem), "Header"),
        };
        menu.Items.Add(wrapper);
    }

    /// <summary>
    /// 测量设备列表的展示高度（行数 × 含边距行距），作为菜单列表区固定高度基准。
    /// 空列表按 1 行占位，最多按 6 行计算（超出部分在固定高度内滚动）。
    /// </summary>
    private double MeasureDeviceListHeight(IReadOnlyList<AudioDevice> devices)
    {
        var probe = new MenuItem { Header = "测量行高" };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // 模拟单行在 StackPanel 平铺时的实际高度（含上下外边距），与列表渲染保持一致
        var rowPanel = new StackPanel();
        rowPanel.Children.Add(new MenuItem { Header = "测量行高", Height = probe.DesiredSize.Height });
        rowPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double rowPitch = rowPanel.DesiredSize.Height;

        // 向下取整只可能压缩最后一行边距，不会裁到文字
        int rows = devices.Count == 0 ? 1 : Math.Min(devices.Count, 6);
        return Math.Floor(rowPitch * rows);
    }

    /// <summary>创建仅显示内容的无边框模板（用于标签按钮与标题行，避免默认按钮/菜单项样式与悬停高亮）。
    /// 最常见的默认参数组合缓存复用，其余组合按需重建。</summary>
    private static ControlTemplate SimpleContentTemplate(Type targetType, string contentSource,
        HorizontalAlignment contentAlignment = HorizontalAlignment.Stretch)
    {
        if (targetType == typeof(MenuItem) && contentSource == "Header" && contentAlignment == HorizontalAlignment.Stretch)
            return SimpleHeaderTemplateInstance;
        return BuildSimpleContentTemplate(targetType, contentSource, contentAlignment);
    }

    private static ControlTemplate BuildSimpleContentTemplate(Type targetType, string contentSource,
        HorizontalAlignment contentAlignment = HorizontalAlignment.Stretch)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, contentSource);
        // 显式指定内容对齐：标题行需拉伸铺满菜单宽度，标签文字需在各自半区内居中
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, contentAlignment);
        return new ControlTemplate(targetType) { VisualTree = presenter };
    }

    /// <summary>设置/退出菜单。</summary>
    private ContextMenu BuildSettingsMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        menu.Items.Add(MakeItem("设置", _openSettings));
        menu.Items.Add(MakeItem("退出", Exit));
        return menu;
    }

    /// <summary>创建与主窗口风格一致的普通菜单项。</summary>
    private static MenuItem MakeItem(string text, Action onClick)
    {
        var item = new MenuItem
        {
            Header = text,
            FontWeight = FontWeights.Normal,
        };
        item.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        item.Click += (_, _) => onClick();
        return item;
    }

    private void SwitchDevice(string deviceId)
    {
        try
        {
            _audio.SetDefaultDevice(deviceId);
            DeviceSwitched?.Invoke();
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"切换设备失败：{ex.Message}", "音频设备切换器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>首次运行提示。</summary>
    public void ShowIntro()
    {
        _tray.ShowBalloon("音频设备切换器", "已驻留托盘，点击图标即可快捷切换音频输出/输入设备。");
    }

    private void Exit()
    {
        Application.Current.Shutdown();
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    public void Dispose()
    {
        ThemeService.ThemeChanged -= UpdateIconTheme;
        UninstallMouseHook();
        _tray.Dispose();
        _host.Close();
    }

    // ---- P/Invoke ----

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
