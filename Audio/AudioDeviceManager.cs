using System.Runtime.InteropServices;

// ============================================================
// 用途：音频设备管理器——基于 Windows Core Audio COM 枚举设备、
// 获取常见默认设备、切换默认输出/输入、启用/禁用设备及音量/静音控制。
// ============================================================
namespace AudioSwitcher.Audio;

/// <summary>
/// 基于 Windows Core Audio（MMDevice / WASAPI）COM 接口：
/// 枚举输出设备、获取默认设备、将指定设备设为默认输出。
/// </summary>
public sealed class AudioDeviceManager : IDisposable
{
    private static readonly Guid ClsidMmeDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid ClsidPolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private static readonly Guid PkeyDeviceFriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int CLSCTX_ALL = 0x17;

    private readonly IMMDeviceEnumerator _enumerator;

    public AudioDeviceManager()
    {
        _enumerator = CreateComObject<IMMDeviceEnumerator>(ClsidMmeDeviceEnumerator);
    }

    /// <summary>枚举当前已启用的输出设备。</summary>
    public IReadOnlyList<AudioDevice> GetOutputDevices() => GetDevices(EDataFlow.Render, DeviceState.Active);

    /// <summary>枚举当前已启用的输入设备。</summary>
    public IReadOnlyList<AudioDevice> GetInputDevices() => GetDevices(EDataFlow.Capture, DeviceState.Active);

    /// <summary>枚举全部输出设备（含已禁用）。</summary>
    public IReadOnlyList<AudioDevice> GetOutputDevicesIncludingDisabled() =>
        GetDevices(EDataFlow.Render, DeviceState.Active | DeviceState.Disabled);

    /// <summary>枚举全部输入设备（含已禁用）。</summary>
    public IReadOnlyList<AudioDevice> GetInputDevicesIncludingDisabled() =>
        GetDevices(EDataFlow.Capture, DeviceState.Active | DeviceState.Disabled);

    private IReadOnlyList<AudioDevice> GetDevices(EDataFlow flow, DeviceState stateMask)
    {
        var result = new List<AudioDevice>();
        ThrowOnFail(_enumerator.EnumAudioEndpoints(flow, (uint)stateMask, out var collection),
            nameof(_enumerator.EnumAudioEndpoints));
        try
        {
            ThrowOnFail(collection.GetCount(out int count), nameof(collection.GetCount));
            for (int i = 0; i < count; i++)
            {
                ThrowOnFail(collection.Item(i, out var device), nameof(collection.Item));
                try
                {
                    var d = ReadDevice(device);
                    if (d != null) result.Add(d);
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        var defId = GetDefaultDevice(flow)?.Id;
        foreach (var d in result) d.IsDefault = d.Id == defId;
        return result;
    }

    /// <summary>获取当前默认输出设备（Console 角色）。</summary>
    public AudioDevice? GetDefaultDevice() => GetDefaultDevice(EDataFlow.Render);

    /// <summary>获取当前默认输入设备（Console 角色）。</summary>
    public AudioDevice? GetDefaultInputDevice() => GetDefaultDevice(EDataFlow.Capture);

    private AudioDevice? GetDefaultDevice(EDataFlow flow)
    {
        var hr = _enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device);
        if (hr != 0 || device == null) return null;
        try
        {
            return ReadDevice(device);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>将指定设备设为默认（输出或输入均可，Console / Multimedia / Communications 三个角色）。</summary>
    public void SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentException("设备 ID 为空", nameof(deviceId));

        var policy = CreateComObject<IPolicyConfig>(ClsidPolicyConfigClient);
        try
        {
            var errors = new List<int>();
            errors.Add(policy.SetDefaultEndpoint(deviceId, ERole.Console));
            errors.Add(policy.SetDefaultEndpoint(deviceId, ERole.Multimedia));
            errors.Add(policy.SetDefaultEndpoint(deviceId, ERole.Communications));

            if (errors.All(e => e != 0))
            {
                var detail = string.Join(", ", errors.Select(e => $"0x{e:X8}"));
                throw new COMException($"设置默认输出设备失败（{detail}）");
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>启用或禁用指定设备（通过 IPolicyConfig 控制设备可见/状态）。</summary>
    public void SetDeviceEnabled(string deviceId, bool enabled)
    {
        if (string.IsNullOrEmpty(deviceId))
            throw new ArgumentException("设备 ID 为空", nameof(deviceId));

        var policy = CreateComObject<IPolicyConfig>(ClsidPolicyConfigClient);
        try
        {
            int hr = policy.SetEndpointVisibility(deviceId, enabled ? 1 : 0);
            if (hr != 0)
                throw new COMException($"{(enabled ? "启用" : "禁用")}设备失败（0x{hr:X8}）");
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>获取默认设备的主音量（0~1）。读取失败返回 null。</summary>
    public float? GetVolume(EDataFlow flow)
    {
        var device = GetDefaultDeviceHandle(flow);
        if (device == null) return null;
        try
        {
            var iid = IidIAudioEndpointVolume;
            var hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var epvPtr);
            if (hr != 0 || epvPtr == IntPtr.Zero) return null;
            var epv = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(epvPtr);
            try
            {
                if (epv.GetMasterVolumeLevelScalar(out float level) != 0) return null;
                return Math.Clamp(level, 0f, 1f);
            }
            finally
            {
                Marshal.Release(epvPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>设置默认设备的主音量（0~1）。</summary>
    public void SetVolume(EDataFlow flow, float scalar)
    {
        var device = GetDefaultDeviceHandle(flow);
        if (device == null) throw new COMException("未找到默认音频设备");
        try
        {
            var iid = IidIAudioEndpointVolume;
            ThrowOnFail(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var epvPtr),
                nameof(device.Activate));
            var epv = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(epvPtr);
            try
            {
                var context = Guid.Empty;
                ThrowOnFail(epv.SetMasterVolumeLevelScalar(Math.Clamp(scalar, 0f, 1f), ref context),
                    nameof(epv.SetMasterVolumeLevelScalar));
            }
            finally
            {
                Marshal.Release(epvPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>获取默认设备是否静音。读取失败返回 null。</summary>
    public bool? GetMute(EDataFlow flow)
    {
        var device = GetDefaultDeviceHandle(flow);
        if (device == null) return null;
        try
        {
            var iid = IidIAudioEndpointVolume;
            var hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var epvPtr);
            if (hr != 0 || epvPtr == IntPtr.Zero) return null;
            var epv = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(epvPtr);
            try
            {
                if (epv.GetMute(out bool mute) != 0) return null;
                return mute;
            }
            finally
            {
                Marshal.Release(epvPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>设置默认设备静音状态（静音不改变音量值，取消静音即恢复原音量）。</summary>
    public void SetMute(EDataFlow flow, bool mute)
    {
        var device = GetDefaultDeviceHandle(flow);
        if (device == null) throw new COMException("未找到默认音频设备");
        try
        {
            var iid = IidIAudioEndpointVolume;
            ThrowOnFail(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var epvPtr),
                nameof(device.Activate));
            var epv = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(epvPtr);
            try
            {
                var context = Guid.Empty;
                ThrowOnFail(epv.SetMute(mute, ref context), nameof(epv.SetMute));
            }
            finally
            {
                Marshal.Release(epvPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private IMMDevice? GetDefaultDeviceHandle(EDataFlow flow)
    {
        var hr = _enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device);
        return hr == 0 ? device : null;
    }

    private static AudioDevice? ReadDevice(IMMDevice device)
    {
        // 设备 ID
        string id;
        ThrowOnFail(device.GetId(out IntPtr idPtr), nameof(device.GetId));
        try
        {
            id = idPtr == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringUni(idPtr) ?? string.Empty);
        }
        finally
        {
            if (idPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(idPtr);
        }

        // 设备状态
        ThrowOnFail(device.GetState(out uint state), nameof(device.GetState));

        // 友好名称
        string name = string.Empty;
        var hr = device.OpenPropertyStore(0 /* STGM_READ */, out var store);
        if (hr == 0 && store != null)
        {
            try
            {
                var key = new PROPERTYKEY { fmtid = PkeyDeviceFriendlyName, pid = 14 };
                if (store.GetValue(ref key, out PROPVARIANT value) == 0)
                {
                    try
                    {
                        if (value.vt == PROPVARIANT.VT_LPWSTR && value.data != IntPtr.Zero)
                            name = Marshal.PtrToStringUni(value.data) ?? string.Empty;
                    }
                    finally
                    {
                        PropVariantClear(ref value);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        return new AudioDevice(id, name, (DeviceState)state);
    }

    private static T CreateComObject<T>(Guid clsid)
    {
        var type = Type.GetTypeFromCLSID(clsid)
            ?? throw new COMException($"无法创建 COM 对象：{clsid}");
        return (T)Activator.CreateInstance(type)!;
    }

    private static void ThrowOnFail(int hr, string operation)
    {
        if (hr != 0)
            throw Marshal.GetExceptionForHR(hr) ?? new COMException($"操作失败：{operation}（0x{hr:X8}）");
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    public void Dispose() => Marshal.ReleaseComObject(_enumerator);
}
