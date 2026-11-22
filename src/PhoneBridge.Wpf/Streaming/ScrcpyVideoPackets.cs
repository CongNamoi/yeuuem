using System.Buffers.Binary;
using System.IO;

namespace PhoneBridge.Wpf.Streaming;

// The 3.3.4 video stream prefixes each packet with flags/timestamp and a big-endian length.
internal sealed class ScrcpyVideoPackets(Stream source)
{
    private readonly byte[] _header = new byte[12];
    private byte[] _payload = [];
    private int _length;
    private bool _configuration;

    public bool HasPacket => _length != 0;

    public async Task AdvanceAsync(CancellationToken token)
    {
        if (await source.ReadAsync(_header.AsMemory(0, 1), token) == 0)
        {
            _length = 0;
            return;
        }

        await source.ReadExactlyAsync(_header.AsMemory(1), token);
        var length = BinaryPrimitives.ReadInt32BigEndian(_header.AsSpan(8));
        if (length is <= 0 or > 32 * 1024 * 1024)
            throw new InvalidDataException("scrcpy returned an invalid video packet length.");
        if (_payload.Length < length) _payload = new byte[length];
        await source.ReadExactlyAsync(_payload.AsMemory(0, length), token);
        _length = length;
        _configuration = (_header[0] & 0x80) != 0;
    }

    public async Task CopySegmentAsync(Stream destination, CancellationToken token)
    {
        // A new configuration after media arrives starts a new encoder sequence.
        // Leave that packet buffered for the next FFmpeg process.
        var sentMedia = false;
        while (HasPacket && (!sentMedia || !_configuration))
        {
            await destination.WriteAsync(_payload.AsMemory(0, _length), token);
            sentMedia |= !_configuration;
            await AdvanceAsync(token);
        }
    }
}
