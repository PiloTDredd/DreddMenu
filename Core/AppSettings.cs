using System.Text.Json;

namespace DreddMenu.Core;

public sealed class AppSettings
{
    /// <summary>"ru" / "en". Пусто — по языку системы.</summary>
    public string Language { get; set; } = "";
    /// <summary>"system" / "dark" / "light".</summary>
    public string Theme { get; set; } = "system";
    public bool ShowPreview { get; set; } = true;
    /// <summary>Проверка системы при первом запуске уже показана.</summary>
    public bool FirstRunDone { get; set; }

    private static string FilePath => Path.Combine(AppInfo.DataFolder, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
