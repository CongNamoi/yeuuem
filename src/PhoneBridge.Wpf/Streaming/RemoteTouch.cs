using System.Buffers.Binary;
using System.IO;
using System.Threading.Channels;

namespace PhoneBridge.Wpf.Streaming;

internal enum RemoteTouchAction : byte { Down = 0, Up = 1, Move = 2 }

internal readonly record struct TouchPoint(int X, int Y, ushort Width, ushort Height);

internal sealed class RemoteTouch
{
    private readonly Channel<(RemoteTouchAction Action, long Id, TouchPoint Point)> _queue =
        Channel.CreateUnbounded<(RemoteTouchAction, long, TouchPoint)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    public bool Send(RemoteTouchAction action, long id, TouchPoint point) =>
        _queue.Writer.TryWrite((action, id, point));

    public void Complete() => _queue.Writer.TryComplete();

    public async Task WriteAsync(Stream stream, CancellationToken token)
    {
        var packet = new byte[32];
        packet[0] = 2; // scrcpy INJECT_TOUCH_EVENT
        await foreach (var touch in _queue.Reader.ReadAllAsync(token))
        {
            packet[1] = (byte)touch.Action;
            BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(2), touch.Id);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(10), touch.Point.X);
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(14), touch.Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18), touch.Point.Width);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(20), touch.Point.Height);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22),
                touch.Action == RemoteTouchAction.Up ? (ushort)0 : ushort.MaxValue);
            // Action button and buttons stay zero for a touchscreen pointer.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try { await stream.WriteAsync(packet, deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new IOException("Timed out sending touch input. Restart the stream.");
            }
        }
    }
}
