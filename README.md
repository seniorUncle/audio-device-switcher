# 音频设备切换器

一个基于 .NET 8 / WPF 的 Windows 桌面音频设备快速切换工具。常驻系统托盘，一键在输出/输入音频设备之间切换，并支持在线调节设备音量。

## 功能特性

- **系统托盘常驻**：最小化后驻留托盘，不影响日常操作
- **输出/输入设备快速切换**：托盘菜单和主窗口均支持切换当前默认设备
- **音量调节**：托盘菜单内直接调节输出/输入设备音量，含百分比显示
- **开机自启**：可选开机自动启动（写入注册表）
- **主窗口左右布局**：左侧输出设备、右侧输入设备，列表可折叠展开
- **跟随系统主题**：背景与高亮色实时同步 Windows 浅色/深色模式，无需重启

## 技术栈

- C# / .NET 8 (net8.0-windows)
- WPF (Windows Presentation Foundation)
- Windows Core Audio API (COM) 设备枚举与音量控制

## 项目结构

```
├── Audio/                 # 音频设备枚举与音量控制（COM封装）
├── Services/              # 托盘、主题、开机自启、图标等
├── Assets/                # 图标资源
├── MainWindow.xaml        # 主窗口界面
├── App.xaml               # 应用全局样式与主题资源
└── AudioSwitcher.csproj
```

## 环境要求

- Windows 10 及以上（x64）
- .NET 8 运行时（或使用 Self-contained 发布版本）

## 构建

```bash
dotnet build -c Release -r win-x64
```

## 主要文件

| 文件 | 说明 |
| --- | --- |
| `Audio/AudioDeviceManager.cs` | 音频设备枚举、默认设备切换、音量读写 |
| `Audio/AudioInterop.cs` | Windows Core Audio COM 接口定义 |
| `Services/TrayIconService.cs` | 托盘菜单构建与交互 |
| `Services/ThemeService.cs` | 跟随系统主题的实时切换 |
| `MainWindow.xaml` | 主界面（输出/输入设备列表、开机自启） |

## 许可证

本项目仅供个人学习使用，未指定开源许可证。