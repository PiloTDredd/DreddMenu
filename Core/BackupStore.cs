using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DreddMenu.Core;

public sealed class BackupItem
{
    public EntryKind Kind { get; set; }
    public RegistryHive Hive { get; set; }
    public string KeyPath { get; set; } = "";
    public string? Clsid { get; set; }
    public string Name { get; set; } = "";
    public bool WasEnabled { get; set; }
}

public sealed class Backup
{
    public DateTime Created { get; set; }
    public List<BackupItem> Items { get; set; } = new();

    [JsonIgnore] public string FilePath { get; set; } = "";
    [JsonIgnore] public string CreatedText => Created.ToString("dd.MM.yyyy  HH:mm:ss");
    [JsonIgnore] public string Summary
    {
        get
        {
            var names = string.Join(", ", Items.Take(4)
                .Select(i => $"{i.Name} ({Loc.T(i.WasEnabled ? "Backup.WasOn" : "Backup.WasOff")})"));
            return Items.Count > 4 ? $"{names} {Loc.F("Backup.More", Items.Count - 4)}" : names;
        }
    }
}

/// <summary>Перед каждым «Применить» сохраняем, как всё было — чтобы можно было откатить.</summary>
public static class BackupStore
{
    public static readonly string Folder = Path.Combine(AppInfo.DataFolder, "Backups");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Save(IEnumerable<MenuEntry> changes)
    {
        var backup = new Backup
        {
            Created = DateTime.Now,
            Items = changes.Select(e => new BackupItem
            {
                Kind = e.Kind, Hive = e.Hive, KeyPath = e.KeyPath, Clsid = e.Clsid, Name = e.Name,
                WasEnabled = e.OriginalEnabled,
            }).ToList(),
        };
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, $"backup_{backup.Created:yyyy-MM-dd_HH-mm-ss-fff}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(backup, Json));
    }

    public static List<Backup> List()
    {
        if (!Directory.Exists(Folder)) return new();
        var list = new List<Backup>();
        foreach (var file in Directory.GetFiles(Folder, "backup_*.json"))
        {
            try
            {
                var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(file), Json);
                if (b == null) continue;
                b.FilePath = file;
                list.Add(b);
            }
            catch { /* повреждённый файл пропускаем */ }
        }
        return list.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>Сколько всего бэкапов и сколько из них не читается.</summary>
    public static (int Total, int Broken) CheckFiles()
    {
        if (!Directory.Exists(Folder)) return (0, 0);
        int total = 0, broken = 0;
        foreach (var file in Directory.GetFiles(Folder, "backup_*.json"))
        {
            total++;
            try { if (JsonSerializer.Deserialize<Backup>(File.ReadAllText(file), Json) == null) broken++; }
            catch { broken++; }
        }
        return (total, broken);
    }

    public static void Restore(Backup backup)
    {
        var doneClsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in backup.Items)
        {
            if (i.Kind == EntryKind.Verb)
            {
                try { RegistryWriter.SetVerbEnabled(i.Hive, i.KeyPath, i.WasEnabled); }
                catch (InvalidOperationException) { /* программу уже удалили — пропускаем */ }
            }
            else if (i.Clsid != null && doneClsids.Add(i.Clsid))
            {
                RegistryWriter.SetHandlerEnabled(i.Clsid, i.Name, i.WasEnabled);
            }
        }
        Native.NotifyShell();
    }

    public static void Delete(Backup backup)
    {
        if (File.Exists(backup.FilePath)) File.Delete(backup.FilePath);
    }
}
