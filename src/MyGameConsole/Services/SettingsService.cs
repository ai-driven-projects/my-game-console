using System.Text.Json;
using System.Text.Json.Serialization;
using MyGameConsole.Models;

namespace MyGameConsole.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyGameConsole");

    public string FilePath => Path.Combine(Directory, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        return Current;
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
    }
}
