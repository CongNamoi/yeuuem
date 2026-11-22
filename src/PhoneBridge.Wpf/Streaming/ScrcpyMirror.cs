using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using PhoneBridge.Core.Adb;

namespace PhoneBridge.Wpf.Streaming;

public sealed record MirrorOptions(int MaxFps, int MaxSize, int BitrateMbps, string Codec);

public sealed class ScrcpyMirror(IAdbClient adb, IProcessRunner runner, ILogger<ScrcpyMirror> log)
{
    public const string ServerVersion = "3.3.4";

    internal async Task RunAsync(string serial, MirrorOptions options, Action<BitmapSource> publish, RemoteTouch touch,
        CancellationToken token)
    {
        var adbPath = adb.ResolveAdbPath() ?? throw new AdbNotFoundException();
        var tools = Path.Combine(AppContext.BaseDirectory, "tools");
        var serverFile = Path.Combine(tools, "scrcpy-server");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        if (!File.Exists(serverFile) || !File.Exists(ffmpeg))
            throw new FileNotFoundException($"Place scrcpy-server {ServerVersion} and ffmpeg.exe in {tools}.");

        var scid = RandomNumberGenerator.GetInt32(int.MaxValue).ToString("x8");
        var remoteFile = $"/data/local/tmp/phonebridge-{scid}.jar";
        int? port = null;
        try
        {
            await AdbAsync(adbPath, serial, token, "push", serverFile, remoteFile);
            // Once requested, finish reading the assigned port even if the window closes during setup.
            var result = await AdbAsync(adbPath, serial, CancellationToken.None,
                "forward", "tcp:0", $"localabstract:scrcpy_{scid}");
            if (!int.TryParse(result, out var assignedPort) || assignedPort is < 1 or > 65535)
                throw new IOException("ADB did not return a valid forwarding port.");
            port = assignedPort;
            token.ThrowIfCancellationRequested();

            using var server = Start(adbPath, "scrcpy", false, "-s", serial, "shell",
                $"CLASSPATH={remoteFile}", "app_process", "/", "com.genymobile.scrcpy.Server",
                ServerVersion, $"scid={scid}", "tunnel_forward=true", "audio=false", "control=true", "clipboard_autosync=false",
                "send_device_meta=false", "send_codec_meta=false", "send_frame_meta=true",
                $"video_codec={options.Codec}", FormattableString.Invariant($"max_fps={options.MaxFps}"),
                FormattableString.Invariant($"max_size={options.MaxSize}"),
                FormattableString.Invariant($"video_bit_rate={options.BitrateMbps * 1_000_000}"));
            try
            {
                using var socket = await ConnectAsync(assignedPort, server, token);
                using var control = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
                await control.ConnectAsync(IPAddress.Loopback, assignedPort, token);
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
                using var inputStop = new CancellationTokenSource();
                var input = touch.WriteAsync(control.GetStream(), inputStop.Token);
                var video = DecodeVideoAsync(socket.GetStream(), ffmpeg, options.Codec, publish, lifetime.Token);
                try
                {
                    log.LogInformation("Started {Codec} mirror for {Serial} with cap {Fps} FPS",
                        options.Codec, serial, options.MaxFps);
                    if (await Task.WhenAny(input, video) == input)
                    {
                        await input;
                        token.ThrowIfCancellationRequested();
                        throw new IOException("The input stream ended. Restart the stream.");
                    }
                    await video;
                    throw new IOException("The video stream ended. Check Diagnostics, then restart the stream.");
                }
                finally
                {
                    lifetime.Cancel();
                    try { await video; }
                    catch (Exception ex) { log.LogDebug(ex, "Video frame reader closed"); }
                    touch.Complete();
                    inputStop.CancelAfter(TimeSpan.FromMilliseconds(500));
                    try { await input; }
                    catch (Exception ex) { log.LogDebug(ex, "Touch writer closed"); }
                }
            }
            finally { Stop(server); }
        }
        finally
        {
            if (port is int forwardedPort)
                await CleanAsync(adbPath, serial, "forward", "--remove", FormattableString.Invariant($"tcp:{forwardedPort}"));
            await CleanAsync(adbPath, serial, "shell", "rm", "-f", remoteFile);
        }
    }

    private static async Task<TcpClient> ConnectAsync(int port, Process server, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var marker = new byte[1];
            while (!server.HasExited)
            {
                var socket = new TcpClient(AddressFamily.InterNetwork);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
                    if (await socket.GetStream().ReadAsync(marker, deadline.Token) == 1 && marker[0] == 0)
                        return socket;
                }
                catch (Exception ex) when (ex is IOException or SocketException)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
                socket.Dispose();
                await Task.Delay(150, deadline.Token);
            }
            throw new IOException($"scrcpy exited during startup (code {server.ExitCode}). Check Diagnostics.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("The scrcpy video socket did not become ready. Check Diagnostics.");
        }
    }

    private async Task<string> AdbAsync(string path, string serial, CancellationToken token, params string[] args)
    {
        var result = await runner.RunAsync(path, ["-s", serial, .. args], TimeSpan.FromSeconds(8), token);
        if (result.TimedOut) throw new TimeoutException($"ADB {args[0]} timed out.");
        if (result.ExitCode != 0) throw new IOException($"ADB {args[0]} failed: {result.StdErr.Trim()}");
        return result.StdOut.Trim();
    }

    private async Task CleanAsync(string path, string serial, params string[] args)
    {
        try { await AdbAsync(path, serial, CancellationToken.None, args); }
        catch (Exception ex) { log.LogWarning(ex, "Could not clean up ADB {Operation} for {Serial}", args[0], serial); }
    }

    private Process Start(string path, string name, bool decoder, params string[] args)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = decoder
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) log.LogWarning("{Tool}: {Message}", name, e.Data);
        };
        if (!decoder)
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) log.LogInformation("{Tool}: {Message}", name, e.Data);
            };
        try
        {
            process.Start();
            process.BeginErrorReadLine();
            if (!decoder) process.BeginOutputReadLine();
            return process;
        }
        catch
        {
            Stop(process);
            process.Dispose();
            throw;
        }
    }

    private void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception ex) { log.LogWarning(ex, "Could not stop video process"); }
    }

    private async Task DecodeVideoAsync(Stream source, string ffmpeg, string codec,
        Action<BitmapSource> publish, CancellationToken token)
    {
        var packets = new ScrcpyVideoPackets(source);
        using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            try { await packets.AdvanceAsync(startup.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("No video packet arrived. Check the phone and Diagnostics.");
            }
        }

        while (packets.HasPacket)
        {
            token.ThrowIfCancellationRequested();
            using var decoder = Start(ffmpeg, "FFmpeg", true, "-hide_banner", "-loglevel", "warning",
                "-probesize", "32768", "-analyzeduration", "1",
                "-f", codec, "-threads", "1", "-i", "pipe:0", "-an",
                "-fps_mode", "passthrough", "-c:v", "bmp", "-pix_fmt", "bgr24",
                "-f", "image2pipe", "pipe:1");
            using var segment = CancellationTokenSource.CreateLinkedTokenSource(token);
            var destination = decoder.StandardInput.BaseStream;
            var feed = packets.CopySegmentAsync(destination, segment.Token);
            var frames = ReadFramesAsync(decoder.StandardOutput.BaseStream, publish, () => Stop(decoder), segment.Token);
            try
            {
                if (await Task.WhenAny(feed, frames) == frames)
                {
                    await frames;
                    if (!feed.IsCompleted)
                        throw new IOException("FFmpeg closed the video pipe unexpectedly. Check Diagnostics.");
                }
                await feed;
                destination.Dispose();
                await frames.WaitAsync(TimeSpan.FromSeconds(3), token);
            }
            finally
            {
                segment.Cancel();
                Stop(decoder);
                try { await Task.WhenAll(feed, frames); }
                catch (Exception ex) { log.LogDebug(ex, "Decoder segment closed"); }
            }
        }
    }

    private static async Task ReadFramesAsync(Stream source, Action<BitmapSource> publish,
        Action interruptRead, CancellationToken token)
    {
        const int headerSize = 14;
        var header = new byte[headerSize];
        var buffer = Array.Empty<byte>();
        using var firstFrame = CancellationTokenSource.CreateLinkedTokenSource(token);
        firstFrame.CancelAfter(TimeSpan.FromSeconds(15));
        // Process pipes on Windows may keep a pending read after token cancellation.
        using var registration = firstFrame.Token.Register(interruptRead);
        try
        {
            while (await source.ReadAsync(header.AsMemory(0, 1), firstFrame.Token) != 0)
            {
                await source.ReadExactlyAsync(header.AsMemory(1), firstFrame.Token);
                var size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(2));
                if (header[0] != 'B' || header[1] != 'M' || size is < 54 or > 48 * 1024 * 1024)
                    throw new InvalidDataException("FFmpeg returned an invalid BMP frame.");
                if (buffer.Length < size) buffer = new byte[size];
                header.CopyTo(buffer, 0);
                await source.ReadExactlyAsync(buffer.AsMemory(headerSize, size - headerSize), firstFrame.Token);
                using var frameData = new MemoryStream(buffer, 0, size, writable: false);
                var decoder = new BmpBitmapDecoder(frameData, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                publish(frame);
                firstFrame.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            firstFrame.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("No video frame arrived. Check the phone and Diagnostics.");
        }
    }
}
