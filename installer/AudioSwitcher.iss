; AudioSwitcher 安装脚本（Inno Setup 7）
; 通过命令行编译：ISCC.exe AudioSwitcher.iss

#define MyAppName "AudioSwitcher"
#define MyAppExeName "AudioSwitcher.exe"
; 应用版本号，与 csproj 中 Version 保持一致（当前 1.0.0）
; 如需调整安装器版本，可修改此值
#define MyAppVersion "1.0.0"
#define MyAppPublisher "AudioSwitcher"
#define MyAppAssocName MyAppName + " File"

[Setup]
; 包基本信息
AppId={{E7C0B5A8-2F32-4F6A-9B7C-0A1B2C3D4E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 默认安装目录
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; 允许安装到 Program Files
PrivilegesRequired=admin
; 输出目录与文件名
OutputDir=Output
OutputBaseFilename=AudioSwitcher-Setup-{#MyAppVersion}
; 压缩设置
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 简体中文
ShowLanguageDialog=no
; 卸载程序
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; 发布的产品主程序
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单程序组
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; 桌面快捷方式（可选，如需保留请保留此行）
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
; 安装完成后可选启动
Filename: "{app}\{#MyAppExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent