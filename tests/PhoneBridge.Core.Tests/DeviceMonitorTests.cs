using Microsoft.Extensions.Logging.Abstractions;
using PhoneBridge.Core.Adb;
using Xunit;

namespace PhoneBridge.Core.Tests;

public class DeviceMonitorTests
{
    private sealed class FakeAdb : IAdbClient
    {
        public Queue<Func<IReadOnlyList<AdbDevice>>> Script { get; } = new();
        public string? ConfiguredPath { get; set; }
        public string? ResolveAdbPath() => "adb";
        public Task<string> GetVersionAsync(CancellationToken ct = default) => Task.FromResult("fake");
        public Task<DeviceDetails> GetDeviceDetailsAsync(string serial, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RestartServerAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken ct = default) => Task.FromResult(Script.Dequeue()());
    }

    [Fact]
    public async Task RaisesOnlyWhenListChanges()
    {
        var fake = new FakeAdb();
        var a = new AdbDevice("a", DeviceState.Device);
        fake.Script.Enqueue(() => new[] { a });
        fake.Script.Enqueue(() => new[] { a });                    // unchanged -> no event
        fake.Script.Enqueue(() => Array.Empty<AdbDevice>());       // unplugged -> event

        var monitor = new DeviceMonitor(fake, NullLogger<DeviceMonitor>.Instance);
        var counts = new List<int>();
        monitor.DevicesUpdated += (_, l) => counts.Add(l.Count);

        await monitor.PollNowAsync();
        await monitor.PollNowAsync();
        await monitor.PollNowAsync();

        Assert.Equal(new[] { 1, 0 }, counts);
    }

    [Fact]
    public async Task AdbMissing_ReportsStatus()
    {
        var fake = new FakeAdb();
        fake.Script.Enqueue(() => throw new AdbNotFoundException());
        var monitor = new DeviceMonitor(fake, NullLogger<DeviceMonitor>.Instance);
        await monitor.PollNowAsync();
        Assert.Equal(AdbStatusKind.AdbNotFound, monitor.Status.Kind);
    }
}
