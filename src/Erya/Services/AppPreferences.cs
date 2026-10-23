using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erya.Services;

/// <summary>
/// Small persisted app preferences (<c>~/.config/Erya/preferences.json</c> on
/// Linux; <c>%APPDATA%\Erya</c> on Windows). Only the frontend choice lives
/// here today — everything else is state derived from the RIME install. Disk
/// failures are swallowed: preferences are a convenience, never a blocker.
/// </summary>
public sealed partial class AppPreferences
{
    private readonly string _path;

    private AppPreferences(string path) => _path = path;

    /// <summary>Id of the frontend the user picked in the switcher; null = auto.</summary>
    public string? DistributionId { get; set; }

    /// <summary>UI language culture name ("zh-Hans"/"zh-Hant"/"en"); null = 简体 base.</summary>
    public string? Language { get; set; }

    public static AppPreferences Load(string? path = null)
    {
        var pathToUse = path ?? DefaultPath();
        var prefs = new AppPreferences(pathToUse);
        try
        {
            if (!File.Exists(pathToUse)) return prefs;
            var data = JsonSerializer.Deserialize(File.ReadAllText(pathToUse), PrefsContext.Default.Persisted);
            if (data is not null)
            {
                prefs.DistributionId = data.DistributionId;
                prefs.Language = data.Language;
            }
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
            File.WriteAllText(_path, JsonSerializer.Serialize(new Persisted(DistributionId, Language), PrefsContext.Default.Persisted));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the preference is sad but must not break the app.
        }
    }

    private static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Erya", "preferences.json");

    private sealed record Persisted(string? DistributionId, string? Language);

    /// <summary>Native AOT 下 System.Text.Json 反射模式不可用，偏好持久化走源生成。</summary>
    [JsonSerializable(typeof(Persisted))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    private sealed partial class PrefsContext : JsonSerializerContext;
}
