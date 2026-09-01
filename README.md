# 音频设备切换器

一个基于 .NET 8 / WPF 的 Windows 桌面音频设备快速切换工具。常驻系统托盘，可通过托盘菜单或主窗口一键在输出/输入音频设备之间切换，并支持在线调节设备音量、启用/禁用设备。

## 功能特性

- **系统托盘常驻**：关闭窗口后驻留托盘，不影响日常操作；托盘图标随系统深浅色自动变色
- **托盘菜单快捷切换**：左键点击托盘图标弹出设备选择菜单，「输出设备 / 输入设备」标签切换，一键设置当前默认设备
- **在线音量调节**：托盘菜单内置输出/输入音量滑块，含百分比显示、逐级音量图标与点击静音
- **设备启用/禁用**：主窗口内支持启用或禁用任一音频设备
- **开机自启**：可选随 Windows 启动，写入注册表（HKCU Run 键）
- **主窗口左右布局**：左侧输出设备、右侧输入设备，已启用/未启用分区可折叠，带平滑展开/收起动画
- **跟随系统主题**：背景与高亮色实时同步 Windows 浅色/深色模式及系统强调色，无需重启
- **亚克力背景**：开启系统「透明效果」时窗口与托盘菜单使用亚克力/毛玻璃背景并圆角化（兼容 Win10 / Win11）
- **Windows 声音设置入口**：标题栏可一键跳转到 Windows 经典「更多声音设置」（mmsys.cpl）
- **单实例运行**：重复启动时自动聚焦已有实例并驻留托盘

## 技术栈

- C# / .NET 8 (net8.0-windows)
- WPF (Windows Presentation Foundation)
- Windows Core Audio API (COM)：设备枚举、默认设备切换、音量/静音控制
- 原生 Win32：Shell_NotifyIcon（托盘图标）、DWM/组合 API（亚克力背景）、Hook（菜单自动收起）

## 项目结构

```
├── Audio/                 # 音频核心：设备枚举、切换、音量控制（COM 封装）
│   ├── AudioDevice.cs         # 设备数据模型
│   ├── AudioDeviceManager.cs  # 设备管理器（枚举/切换/启用禁用/音量/静音）
│   └── AudioInterop.cs        # Core Audio COM 接口 P/Invoke 定义
├── Services/              # 应用服务层
│   ├── AutoStart.cs           # 开机自启与首启提示标记
│   ├── Backdrop.cs            # 亚克力/毛玻璃背景与窗口圆角
│   ├── IconFactory.cs         # 图标生成与主题着色（含缓存）
│   ├── ThemeService.cs        # 系统主题/强调色实时同步
│   ├── TrayIcon.cs            # 原生托盘图标（Shell_NotifyIcon）封装
│   └── TrayIconService.cs     # 托盘菜单构建、定位与交互
├── Assets/                # 图标资源（应用图标、音量/麦克风/Windows 图标）
├── MainWindow.xaml(.cs)   # 主设置窗口界面与逻辑
├── App.xaml(.cs)          # 应用全局样式、主题资源与启动入口
├── AudioSwitcher.csproj
└── app.manifest
```

## 环境要求

- Windows 10 及以上（x64）
- .NET 8 运行时（或使用 Self-contained 单文件发布，无需安装运行时）

## 构建

```bash
# 常规构建
dotnet build -c Release -r win-x64

# 单文件独立发布（Self-contained）
dotnet publish -c Release -r win-x64
```

## 使用说明

1. 启动后应用驻留系统托盘，首次运行会弹出使用提示
2. 左键点击托盘图标 → 弹出设备选择菜单，切换输出/输入设备
3. 主窗口中点击设备卡片可设为默认，右侧开关可启用/禁用对应设备
4. 告警未能读取设备时，请检查系统音频服务是否正常运行

## 许可证

本项目仅供个人学习使用，未指定开源许可证。