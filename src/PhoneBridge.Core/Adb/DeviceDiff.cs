namespace PhoneBridge.Core.Adb;

public sealed record DeviceChanges(
    IReadOnlyList<AdbDevice> Added,
    IReadOnlyList<AdbDevice> Removed,
    IReadOnlyList<AdbDevice> Changed)
{
    public bool Any => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;
}

public static class DeviceDiff
{
    public static DeviceChanges Compute(IReadOnlyList<AdbDevice> previous, IReadOnlyList<AdbDevice> next)
    {
        var prev = previous.ToDictionary(d => d.Serial);
        var nxt = next.ToDictionary(d => d.Serial);
        var added = next.Where(d => !prev.ContainsKey(d.Serial)).ToList();
        var removed = previous.Where(d => !nxt.ContainsKey(d.Serial)).ToList();
        var changed = next.Where(d => prev.TryGetValue(d.Serial, out var p) && p != d).ToList();
        return new DeviceChanges(added, removed, changed);
    }
}
