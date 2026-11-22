using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PhoneBridge.Wpf.Services;

public sealed class AppSettings
{
    public string? AdbPath { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly ILogger<SettingsService> _log;
    private readonly string _path;

    public SettingsService(ILogger<SettingsService> log)
    {
        _log = log;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoneBridge");
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();
    public string FilePath => _path;

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read settings; using defaults");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, Json));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not save settings");
        }
    }
}
