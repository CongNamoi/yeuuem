namespace PhoneBridge.Core.Adb;

public interface IAdbLocator
{
    string? Locate(string? configuredPath);
}

public sealed class AdbLocator : IAdbLocator
{
    public string? Locate(string? configuredPath)
    {
        var exe = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        foreach (var c in Candidates(configuredPath, exe))
        {
            try { if (File.Exists(c)) return Path.GetFullPath(c); } catch { /* invalid path */ }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string? configured, string exe)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Directory.Exists(configured) ? Path.Combine(configured, exe) : configured;
        }

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "tools", "platform-tools", exe);
        yield return Path.Combine(baseDir, "tools", "scrcpy", exe);
        yield return Path.Combine(baseDir, exe);

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim().Trim('"'), exe);

        foreach (var v in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var sdk = Environment.GetEnvironmentVariable(v);
            if (!string.IsNullOrWhiteSpace(sdk)) yield return Path.Combine(sdk, "platform-tools", exe);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "Android", "Sdk", "platform-tools", exe);
    }
}
