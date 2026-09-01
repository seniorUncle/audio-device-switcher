using System.Runtime.InteropServices;

// ============================================================
// 用途：Windows Core Audio（MMDevice / PolicyConfig）COM 接口、
// 枚举、结构体的 P/Invoke 定义，供 AudioDeviceManager 调用。
// ============================================================
namespace AudioSwitcher.Audio;

/// <summary>音频数据流方向（EDataFlow）。</summary>
public enum EDataFlow { Render = 0, Capture = 1, All = 2 }

/// <summary>默认设备角色（ERole）。</summary>
public enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

/// <summary>设备状态掩码（DEVICE_STATE）。</summary>
[Flags]
public enum DeviceState : uint
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
    All = 0xF,
}

/// <summary>PROPERTYKEY（用于读取设备属性）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

/// <summary>最小 PROPVARIANT（仅用于读取 VT_LPWSTR 字符串属性）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr data;

    public const ushort VT_LPWSTR = 31;
}

/// <summary>IMMDeviceEnumerator（COM：CLSID_MMDeviceEnumerator）。</summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback([MarshalAs(UnmanagedType.IUnknown)] object client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback([MarshalAs(UnmanagedType.IUnknown)] object client);
}

/// <summary>IMMDeviceCollection。</summary>
[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int Item(int index, out IMMDevice device);
}

/// <summary>IMMDevice。</summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);

    [PreserveSig]
    int OpenPropertyStore(int stgmAccess, out IPropertyStore store);

    [PreserveSig]
    int GetId(out IntPtr idPtr);

    [PreserveSig]
    int GetState(out uint state);
}

/// <summary>IPropertyStore（读取设备友好名称）。</summary>
[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetAt(int index, out PROPERTYKEY key);

    [PreserveSig]
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);

    [PreserveSig]
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);

    [PreserveSig]
    int Commit();
}

/// <summary>IAudioEndpointVolume（控制端点主音量）。</summary>
[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr pNotify);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float level, ref Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float level);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float level);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float min, out float max, out float increment);
}

/// <summary>
/// IPolicyConfig（未公开 COM 接口，用于将设备设为默认输出，无需重启音频服务）。
/// 方法顺序必须严格匹配 vtable，不可调整。
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mixFormat);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int @default, IntPtr deviceFormat);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int @default, out long value, out long defaultValue);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, long value);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int @default, ref PROPERTYKEY key, IntPtr value);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int @default, ref PROPERTYKEY key, ref PROPVARIANT value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}
