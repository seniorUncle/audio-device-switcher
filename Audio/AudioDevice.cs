namespace AudioSwitcher.Audio;

/// <summary>音频输出设备模型。</summary>
public sealed class AudioDevice
{
    public AudioDevice(string id, string name, DeviceState state)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "未知设备" : name;
        State = state;
    }

    /// <summary>设备唯一 ID（用于切换）。</summary>
    public string Id { get; }

    /// <summary>设备友好名称。</summary>
    public string Name { get; }

    /// <summary>设备状态。</summary>
    public DeviceState State { get; }

    /// <summary>是否为当前默认输出设备（由管理器标记）。</summary>
    public bool IsDefault { get; set; }

    /// <summary>是否可用（已插入且启用）。</summary>
    public bool IsActive => State == DeviceState.Active;

    /// <summary>状态中文描述。</summary>
    public string StateText => State switch
    {
        DeviceState.Active => "已启用",
        DeviceState.Disabled => "已禁用",
        DeviceState.NotPresent => "已移除",
        DeviceState.Unplugged => "未插入",
        _ => "未知",
    };

    public override string ToString() => Name;
}
