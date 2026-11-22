using PhoneBridge.Core.Adb;
using Xunit;

namespace PhoneBridge.Core.Tests;

public class AdbOutputParserTests
{
    private const string Sample =
        "* daemon not running; starting now at tcp:5037\r\n" +
        "* daemon started successfully\r\n" +
        "List of devices attached\r\n" +
        "1a2b3c4d               device usb:1-2 product:vermeer model:23117RK66C device:vermeer transport_id:1\r\n" +
        "emulator-5554          unauthorized transport_id:2\r\n" +
        "R5CT123                offline\r\n" +
        "ABC123                 no permissions (user in plugdev group; are you root?); see [http://developer.android.com/tools/device.html] usb:1-1 transport_id:3\r\n" +
        "\r\n";

    [Fact]
    public void ParseDevices_ReadsAllDevicesAndSkipsNoise()
    {
        var devices = AdbOutputParser.ParseDevices(Sample);
        Assert.Equal(4, devices.Count);
    }

    [Fact]
    public void ParseDevices_ReadsReadyDeviceFields()
    {
        var d = AdbOutputParser.ParseDevices(Sample)[0];
        Assert.Equal("1a2b3c4d", d.Serial);
        Assert.Equal(DeviceState.Device, d.State);
        Assert.Equal("23117RK66C", d.Model);
        Assert.Equal("vermeer", d.DeviceCodename);
        Assert.Equal("1", d.TransportId);
        Assert.True(d.IsReady);
    }

    [Fact]
    public void ParseDevices_MapsStates()
    {
        var list = AdbOutputParser.ParseDevices(Sample);
        Assert.Equal(DeviceState.Unauthorized, list[1].State);
        Assert.Equal(DeviceState.Offline, list[2].State);
        Assert.Equal(DeviceState.NoPermissions, list[3].State);
        Assert.Equal("3", list[3].TransportId);
    }

    [Fact]
    public void ParseDevices_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(AdbOutputParser.ParseDevices("List of devices attached\r\n\r\n"));
    }

    [Fact]
    public void ParseGetProp_ParsesBracketedPairs()
    {
        var p = AdbOutputParser.ParseGetProp("[ro.product.model]: [23117RK66C]\r\n[ro.empty]: []\r\ngarbage\r\n");
        Assert.Equal("23117RK66C", p["ro.product.model"]);
        Assert.Equal(string.Empty, p["ro.empty"]);
        Assert.Equal(2, p.Count);
    }

    [Fact]
    public void ParseWmSize_PrefersOverride()
    {
        var s = AdbOutputParser.ParseWmSize("Physical size: 1220x2712\r\nOverride size: 720x1600\r\n");
        Assert.Equal((720, 1600), s);
    }

    [Fact]
    public void ParseWmSize_PhysicalOnly()
    {
        Assert.Equal((1220, 2712), AdbOutputParser.ParseWmSize("Physical size: 1220x2712"));
    }

    [Fact]
    public void ParseWmDensity_Works()
    {
        Assert.Equal(480, AdbOutputParser.ParseWmDensity("Physical density: 480"));
        Assert.Equal(320, AdbOutputParser.ParseWmDensity("Physical density: 480\nOverride density: 320"));
        Assert.Null(AdbOutputParser.ParseWmDensity("nonsense"));
    }
}
