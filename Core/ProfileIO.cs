using System.Text.Json;
using Microsoft.Win32;

namespace DreddMenu.Core;

/// <summary>Экспорт/импорт настроек меню — чтобы перенести на другой ПК.</summary>
public static class ProfileIO
{
    public sealed class Profile
    {
        public string App { get; set; } = "Dredd Menu";
        public int Version { get; set; } = 1;
        public DateTime Created { get; set; } = DateTime.Now;
        public List<State> States { get; set; } = new();
        public List<Custom> Custom { get; set; } = new();
    }

    public sealed class State
    {
        public EntryKind Kind { get; set; }
        public RegistryHive Hive { get; set; }
        public string KeyPath { get; set; } = "";
        public string? Clsid { get; set; }
        public string Name { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public sealed class Custom
    {
        public string KeyPath { get; set; } = "";
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string? Icon { get; set; }
        public bool ShiftOnly { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Export(string file, IEnumerable<MenuEntry> entries)
    {
        var p = new Profile();
        foreach (var e in entries)
        {
            if (e.IsCustom)
            {
                if (CustomItems.Read(e.KeyPath) is { } c)
                    p.Custom.Add(new Custom { KeyPath = e.KeyPath, Name = c.Name, Command = c.Command, Icon = c.Icon, ShiftOnly = c.Shift });
            }
            p.States.Add(new State { Kind = e.Kind, Hive = e.Hive, KeyPath = e.KeyPath, Clsid = e.Clsid, Name = e.Name, Enabled = e.IsEnabled });
        }
        File.WriteAllText(file, JsonSerializer.Serialize(p, Json));
    }

    public static Profile Load(string file) =>
        JsonSerializer.Deserialize<Profile>(File.ReadAllText(file), Json) ?? throw new InvalidDataException();

    /// <summary>Создаёт недостающие свои пункты. Возвращает true, если что-то создано (нужно пересканировать).</summary>
    public static bool CreateMissingCustom(Profile p)
    {
        bool created = false;
        foreach (var c in p.Custom)
        {
            if (!c.KeyPath.StartsWith(@"Software\Classes\", StringComparison.OrdinalIgnoreCase) || CustomItems.Exists(c.KeyPath)) continue;
            CustomItems.Write(c.KeyPath, c.Name, c.Command, c.Icon, c.ShiftOnly);
            created = true;
        }
        if (created) Native.NotifyShell();
        return created;
    }

    /// <summary>Выставляет тумблеры по профилю (без записи в реестр). Возвращает число изменений.</summary>
    public static int ApplyStates(Profile p, IReadOnlyList<MenuEntry> entries)
    {
        int changed = 0;
        foreach (var s in p.States)
        {
            var matches = s.Kind == EntryKind.Handler && s.Clsid != null
                ? entries.Where(e => e.Kind == EntryKind.Handler && string.Equals(e.Clsid, s.Clsid, StringComparison.OrdinalIgnoreCase))
                : entries.Where(e => e.Kind == EntryKind.Verb && e.Hive == s.Hive && string.Equals(e.KeyPath, s.KeyPath, StringComparison.OrdinalIgnoreCase));
            foreach (var e in matches)
            {
                if (e.IsEnabled == s.Enabled) continue;
                e.IsEnabled = s.Enabled;
                changed++;
            }
        }
        return changed;
    }
}
