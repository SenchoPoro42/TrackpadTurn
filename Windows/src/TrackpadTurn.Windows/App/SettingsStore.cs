using System.Text.Json;
using TrackpadTurn.Core;
using TrackpadTurn.Windows.Diagnostics;

namespace TrackpadTurn.Windows.App;

/// <summary>Everything the app remembers, saved as JSON.</summary>
internal sealed record AppSettings
{
    public TurnSettings Turn { get; init; } = new();
    /// <summary>A WoW install outside Program Files, picked by the user.</summary>
    public string? CustomWowFolder { get; init; }
    public bool DiagnosticLog { get; init; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> in %APPDATA%\TrackpadTurn\settings.json.</summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string path;

    public SettingsStore()
    {
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrackpadTurn", "settings.json");
        IsFirstRun = !File.Exists(path);
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    /// <summary>No settings were saved before: show the window so the user finds the addon install.</summary>
    public bool IsFirstRun { get; }

    /// <summary>Raised on the UI thread after every change.</summary>
    public event Action<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var updated = change(Settings);
        if (updated == Settings)
        {
            return;
        }
        Settings = updated;
        Save();
        Changed?.Invoke(updated);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
                // Fields missing from an older file keep their defaults (System.Text.Json leaves
                // initializers alone); speeds are clamped like the Mac app's.
                return loaded with { Turn = (loaded.Turn ?? new TurnSettings()).Normalized() };
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error("settings could not be read; using defaults", exception);
        }
        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("settings could not be saved", exception);
        }
    }
}
