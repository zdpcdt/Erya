using System;
using System.IO;
using System.Text.Json;

namespace Erya.Services;

/// <summary>
/// Small persisted app preferences (<c>~/.config/Erya/preferences.json</c> on
/// Linux; <c>%APPDATA%\Erya</c> on Windows). Only the frontend choice lives
/// here today — everything else is state derived from the RIME install. Disk
/// failures are swallowed: preferences are a convenience, never a blocker.
/// </summary>
public sealed class AppPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    private AppPreferences(string path) => _path = path;

    /// <summary>Id of the frontend the user picked in the switcher; null = auto.</summary>
    public string? DistributionId { get; set; }

    public static AppPreferences Load(string? path = null)
    {
        var pathToUse = path ?? DefaultPath();
        var prefs = new AppPreferences(pathToUse);
        try
        {
            if (!File.Exists(pathToUse)) return prefs;
            var data = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(pathToUse));
            if (data is not null) prefs.DistributionId = data.DistributionId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable preferences file falls back to defaults.
        }
        return prefs;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new Persisted(DistributionId), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the preference is sad but must not break the app.
        }
    }

    private static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Erya", "preferences.json");

    private sealed record Persisted(string? DistributionId);
}
