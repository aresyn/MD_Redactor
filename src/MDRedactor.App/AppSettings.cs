using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDRedactor.App;

internal sealed record AppSettings
{
    public AppThemePreference Theme { get; init; } = AppThemePreference.System;

    public AppLanguagePreference Language { get; init; } = AppLanguagePreference.System;
}

internal enum AppThemePreference
{
    System,
    Light,
    Dark
}

internal enum AppLanguagePreference
{
    System,
    Russian,
    English
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    static AppSettingsStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.LogError(ex, "Could not read app settings");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = GetSettingsPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError(ex, "Could not save app settings");
        }
    }

    private static string GetSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "MDRedactor", "settings.json");
    }
}
