namespace PhoneBridge.Core.Adb;

public enum DeviceState { Unknown, Device, Unauthorized, Offline, NoPermissions, Recovery, Bootloader, Sideload }

public sealed record AdbDevice(
    string Serial,
    DeviceState State,
    string? Model = null,
    string? Product = null,
    string? DeviceCodename = null,
    string? TransportId = null)
{
    public bool IsReady => State == DeviceState.Device;
    public bool IsWireless => Serial.Contains(':') || Serial.Contains("_adb-tls", StringComparison.Ordinal);
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : Model.Replace('_', ' ');
    public string StateText => DeviceStateDescriber.Label(State);
}

public sealed record DeviceDetails(
    string Serial,
    string? Manufacturer,
    string? Brand,
    string? Model,
    string? AndroidRelease,
    int? SdkLevel,
    string? HyperOsVersion,
    string? MiuiVersion,
    string? CpuAbi,
    string? SecurityPatch,
    int? ScreenWidth,
    int? ScreenHeight,
    int? Density)
{
    public string ScreenText => ScreenWidth is { } w && ScreenHeight is { } h ? $"{w} x {h}" : "unknown";
    public string AndroidText => AndroidRelease is null ? "unknown" : $"Android {AndroidRelease} (API {SdkLevel?.ToString() ?? "?"})";
    public string OsSkinText => HyperOsVersion ?? MiuiVersion ?? "not detected";
}

public static class DeviceStateDescriber
{
    public static string Label(DeviceState s) => s switch
    {
        DeviceState.Device => "Ready",
        DeviceState.Unauthorized => "Unauthorized",
        DeviceState.Offline => "Offline",
        DeviceState.NoPermissions => "No permissions",
        DeviceState.Recovery => "Recovery mode",
        DeviceState.Bootloader => "Bootloader",
        DeviceState.Sideload => "Sideload",
        _ => "Unknown"
    };

    public static string Hint(DeviceState s) => s switch
    {
        DeviceState.Device => "Device is authorized and ready.",
        DeviceState.Unauthorized => "USB debugging is on but this PC is not trusted. Unlock the phone and accept the \"Allow USB debugging?\" prompt (tick \"Always allow\").",
        DeviceState.Offline => "adb sees the device but cannot talk to it. Replug the cable, toggle USB debugging, or use Restart adb.",
        DeviceState.NoPermissions => "Windows/adb lacks permission for this device. Install the Xiaomi/Google USB driver and restart adb.",
        DeviceState.Recovery => "Device is in recovery mode. Reboot into Android.",
        DeviceState.Bootloader => "Device is in bootloader/fastboot mode. Reboot into Android.",
        DeviceState.Sideload => "Device is in sideload mode. Reboot into Android.",
        _ => "Unknown device state. Replug the cable and press Refresh."
    };
}

public class AdbException : Exception
{
    public AdbException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class AdbNotFoundException : AdbException
{
    public AdbNotFoundException() : base("adb.exe was not found. Install Android platform-tools, place it in tools\\platform-tools next to the app, or set the path in Settings.") { }
}
