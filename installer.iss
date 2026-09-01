; 音频设备切换器 安装脚本（Inno Setup 6）
; 依赖系统已安装的 .NET 8 桌面运行时（框架依赖发布，体积小）

#define MyAppName "音频设备切换器"
#define MyAppNameEnglish "AudioSwitcher"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "AudioSwitcher"
#define MyAppExeName "AudioSwitcher.exe"

[Setup]
AppId={{B1A3D9E5-7C22-4F3E-9A0D-6C8E4D1F2A3B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
SetupIconFile=installer-icon-classic.ico
DefaultDirName={userpf}\{#MyAppNameEnglish}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=AudioSwitcher-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\installer-icon.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Files]
Source: "publish-fd\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "installer-icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\installer-icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\installer-icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// 比较点分版本号：A<B 返回 -1，相等返回 0，A>B 返回 1
function CompareVersion(A, B: String): Integer;
var
  p1, p2, n1, n2: Integer;
  restA, restB: String;
begin
  Result := 0;
  restA := A;
  restB := B;
  while (restA <> '') or (restB <> '') do
  begin
    p1 := Pos('.', restA);
    p2 := Pos('.', restB);
    if p1 = 0 then p1 := Length(restA) + 1;
    if p2 = 0 then p2 := Length(restB) + 1;
    n1 := StrToIntDef(Copy(restA, 1, p1 - 1), 0);
    n2 := StrToIntDef(Copy(restB, 1, p2 - 1), 0);
    if n1 < n2 then begin Result := -1; Exit; end;
    if n1 > n2 then begin Result := 1; Exit; end;
    restA := Copy(restA, p1 + 1, Length(restA));
    restB := Copy(restB, p2 + 1, Length(restB));
  end;
end;

// 检查系统是否已安装 .NET 8 桌面运行时（Microsoft.WindowsDesktop.App >= 8.0.0）
// 优先看注册表登记；部分安装途径（如 Visual Studio、手动放置）不写注册表，
// 此时扫描共享运行时目录下是否存在 >= 8.0 的版本子目录。
function IsDesktopRuntimeInstalled(): Boolean;
var
  V, BaseDir, VerStr: String;
  FindRec: TFindRec;
begin
  Result := False;

  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\Microsoft.WindowsDesktop.App', 'Version', V) then
    Result := (CompareVersion(V, '8.0.0') >= 0);
  if not Result then
    if RegQueryStringValue(HKCU64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\Microsoft.WindowsDesktop.App', 'Version', V) then
      Result := (CompareVersion(V, '8.0.0') >= 0);
  if Result then Exit;

  BaseDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BaseDir) then
    BaseDir := ExpandConstant('{userpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(BaseDir) then
  begin
    if FindFirst(BaseDir + '\*', FindRec) then
    begin
      try
        repeat
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            VerStr := FindRec.Name;
            if (CompareVersion(VerStr, '8.0.0') >= 0) then
            begin
              Result := True;
              Exit;
            end;
          end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  if not IsDesktopRuntimeInstalled() then
    MsgBox('未检测到 .NET 8 桌面运行时（Microsoft.WindowsDesktop.App 8.0+）。' + #13#10 +
           '程序需要该运行时才能运行，可前往 https://dotnet.microsoft.com/download/dotnet/8.0 安装后重试。',
           mbInformation, MB_OK);
  Result := True;
end;
