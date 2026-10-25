using System.Text.Json;

namespace PortableDictionary.Core;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public float TextSize { get; set; } = 10f;
    public int WindowWidth { get; set; } = 720;
    public int WindowHeight { get; set; } = 580;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public string Shortcut { get; set; } = "Ctrl+Alt+D";

    public void Normalize()
    {
        TextSize = Math.Clamp(TextSize, 9f, 20f);
        WindowWidth = Math.Clamp(WindowWidth, 420, 10000);
        WindowHeight = Math.Clamp(WindowHeight, 320, 10000);
        if (string.IsNullOrWhiteSpace(Shortcut))
        {
            Shortcut = "Ctrl+Alt+D";
        }
    }
}

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

public sealed class PortableSettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _writeWarningIssued;

    public string SettingsPath => settingsPath;

    public SettingsLoadResult Load()
    {
        if (!File.Exists(settingsPath))
        {
            return new SettingsLoadResult(new AppSettings(), null);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath));
            if (settings is null || settings.SchemaVersion != 1)
            {
                throw new JsonException("Unsupported settings schema.");
            }
            settings.Normalize();
            return new SettingsLoadResult(settings, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SettingsLoadResult(
                new AppSettings(),
                "settings.json could not be read. Defaults are in use; no alternate settings location was used.");
        }
    }

    public bool Save(AppSettings settings, out string? warning)
    {
        warning = null;
        settings.Normalize();
        var temporaryPath = settingsPath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, settingsPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Delete(temporaryPath); } catch { /* Best-effort temporary-file cleanup. */ }
            if (!_writeWarningIssued)
            {
                warning = "Could not write settings.json beside Dictionary.exe. Settings remain in memory for this session.";
                _writeWarningIssued = true;
            }
            return false;
        }
    }
}
