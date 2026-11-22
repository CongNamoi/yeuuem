using System.Globalization;
using System.Text.RegularExpressions;

namespace PhoneBridge.Core.Adb;

public static class AdbOutputParser
{
    private static readonly Regex PropRegex = new(@"^\[(?<k>.+?)\]:\s*\[(?<v>.*)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"(?<w>\d+)x(?<h>\d+)", RegexOptions.Compiled);
    private static readonly Regex KeyRegex = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    public static IReadOnlyList<AdbDevice> ParseDevices(string output)
    {
        var list = new List<AdbDevice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
                continue;

            int ws = IndexOfWhitespace(line);
            if (ws <= 0) continue;

            var serial = line[..ws];
            var rest = line[ws..].Trim();
            DeviceState state;
            string props;

            if (rest.StartsWith("no permissions", StringComparison.OrdinalIgnoreCase))
            {
                state = DeviceState.NoPermissions;
                props = rest;
            }
            else
            {
                int ws2 = IndexOfWhitespace(rest);
                var token = ws2 < 0 ? rest : rest[..ws2];
                props = ws2 < 0 ? string.Empty : rest[ws2..];
                state = MapState(token);
            }

            var kv = ParseKeyValues(props);
            kv.TryGetValue("model", out var model);
            kv.TryGetValue("product", out var product);
            kv.TryGetValue("device", out var device);
            kv.TryGetValue("transport_id", out var tid);
            list.Add(new AdbDevice(serial, state, model, product, device, tid));
        }
        return list;
    }

    public static DeviceState MapState(string token) => token.ToLowerInvariant() switch
    {
        "device" => DeviceState.Device,
        "unauthorized" => DeviceState.Unauthorized,
        "offline" => DeviceState.Offline,
        "recovery" => DeviceState.Recovery,
        "bootloader" or "fastboot" => DeviceState.Bootloader,
        "sideload" => DeviceState.Sideload,
        _ => DeviceState.Unknown
    };

    public static Dictionary<string, string> ParseGetProp(string output)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n'))
        {
            var m = PropRegex.Match(raw.Trim());
            if (m.Success) d[m.Groups["k"].Value] = m.Groups["v"].Value;
        }
        return d;
    }

    /// <summary>Parses "wm size" output. An override size wins over the physical size.</summary>
    public static (int Width, int Height)? ParseWmSize(string output)
    {
        (int, int)? physical = null, over = null;
        foreach (var raw in output.Split('\n'))
        {
            var m = SizeRegex.Match(raw);
            if (!m.Success) continue;
            var v = (int.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture));
            if (raw.Contains("Override", StringComparison.OrdinalIgnoreCase)) over = v;
            else if (raw.Contains("Physical", StringComparison.OrdinalIgnoreCase)) physical = v;
        }
        return over ?? physical;
    }

    public static int? ParseWmDensity(string output)
    {
        int? physical = null, over = null;
        foreach (var raw in output.Split('\n'))
        {
            var idx = raw.LastIndexOf(':');
            if (idx < 0 || !int.TryParse(raw[(idx + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) continue;
            if (raw.Contains("Override", StringComparison.OrdinalIgnoreCase)) over = n;
            else if (raw.Contains("Physical", StringComparison.OrdinalIgnoreCase)) physical = n;
        }
        return over ?? physical;
    }

    private static Dictionary<string, string> ParseKeyValues(string s)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tok in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int i = tok.IndexOf(':');
            if (i <= 0) continue;
            var key = tok[..i];
            if (!KeyRegex.IsMatch(key)) continue;
            d[key] = tok[(i + 1)..];
        }
        return d;
    }

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return i;
        return -1;
    }
}
