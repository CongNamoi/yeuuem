using PhoneBridge.Core.Adb;
using Xunit;

namespace PhoneBridge.Core.Tests;

public class DeviceDiffTests
{
    private static AdbDevice Dev(string s, DeviceState st = DeviceState.Device) => new(s, st);

    [Fact]
    public void DetectsAddedRemovedChanged()
    {
        var prev = new[] { Dev("a"), Dev("b", DeviceState.Unauthorized), Dev("c") };
        var next = new[] { Dev("b"), Dev("c"), Dev("d") };
        var diff = DeviceDiff.Compute(prev, next);
        Assert.Equal(new[] { "d" }, diff.Added.Select(x => x.Serial));
        Assert.Equal(new[] { "a" }, diff.Removed.Select(x => x.Serial));
        Assert.Equal(new[] { "b" }, diff.Changed.Select(x => x.Serial));
        Assert.True(diff.Any);
    }

    [Fact]
    public void IdenticalLists_NoChanges()
    {
        var l = new[] { Dev("a") };
        Assert.False(DeviceDiff.Compute(l, new[] { Dev("a") }).Any);
    }
}
