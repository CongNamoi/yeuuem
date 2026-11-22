using System.Diagnostics;

namespace PhoneBridge.Core.Adb;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string[] args, TimeSpan timeout, CancellationToken ct = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string[] args, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var outTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var errTask = proc.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return new ProcessResult(proc.ExitCode, await outTask.ConfigureAwait(false), await errTask.ConfigureAwait(false), false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            _ = outTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            _ = errTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (ct.IsCancellationRequested) throw;
            return new ProcessResult(-1, string.Empty, string.Empty, true);
        }
    }
}
