using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DreddMenu.Core;

/// <summary>Свои пункты меню, которые создаёт пользователь. Лежат в HKCU, ключи начинаются с префикса.</summary>
public static class CustomItems
{
    public const string Prefix = "DreddMenu.";

    public sealed record Target(string Id, string ClassPath, bool Background, string LabelKey);

    public static readonly Target[] Targets =
    {
        new("background", @"Directory\Background", true, "Add.Where.Background"),
        new("folders", "Directory", false, "Add.Where.Folders"),
        new("files", "*", false, "Add.Where.Files"),
        new("drives", "Drive", false, "Add.Where.Drives"),
    };

    public sealed record Template(string NameKey, string Exe, string Args, string[] Targets);

    /// <summary>Готовые шаблоны — только те, чьи программы реально установлены.</summary>
    public static List<Template> AvailableTemplates()
    {
        var list = new List<Template>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);

        var wt = Path.Combine(local, @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(wt)) list.Add(new("Tpl.Terminal", wt, "-d \"{path}\"", new[] { "background", "folders", "drives" }));

        var code = new[] { Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"), Path.Combine(pf, @"Microsoft VS Code\Code.exe") }
            .FirstOrDefault(File.Exists);
        if (code != null) list.Add(new("Tpl.VSCode", code, "\"{path}\"", new[] { "background", "folders", "files" }));

        list.Add(new("Tpl.Cmd", Path.Combine(sys, "cmd.exe"), "/s /k pushd \"{path}\"", new[] { "background", "folders", "drives" }));
        list.Add(new("Tpl.PowerShell", Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoExit -Command Set-Location -LiteralPath '{path}'", new[] { "background", "folders", "drives" }));
        list.Add(new("Tpl.Notepad", Path.Combine(sys, "notepad.exe"), "\"{path}\"", new[] { "files" }));
        return list;
    }

    public static void Create(string name, string exe, string args, IEnumerable<string> targetIds, bool shiftOnly)
    {
        var slug = Regex.Replace(name, @"[^\p{L}\p{Nd}]+", "_").Trim('_');
        if (slug.Length > 30) slug = slug[..30];
        var keyName = $"{Prefix}{slug}.{Guid.NewGuid().ToString("N")[..6]}";

        foreach (var t in Targets.Where(t => targetIds.Contains(t.Id)))
        {
            var finalArgs = args.Replace("{path}", t.Background ? "%V" : "%1");
            Write($@"Software\Classes\{t.ClassPath}\shell\{keyName}", name, $"\"{exe}\" {finalArgs}".Trim(), exe, shiftOnly);
        }
        Native.NotifyShell();
    }

    /// <summary>Записать пункт как есть (используется при импорте профиля).</summary>
    public static void Write(string keyPath, string name, string command, string? icon, bool shiftOnly)
    {
        using var k = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        k.SetValue("MUIVerb", name);
        if (!string.IsNullOrWhiteSpace(icon)) k.SetValue("Icon", icon);
        if (shiftOnly) k.SetValue("Extended", "");
        else k.DeleteValue("Extended", false);
        using var c = k.CreateSubKey("command", writable: true);
        c.SetValue("", command);
    }

    public static bool Exists(string keyPath)
    {
        using var k = Registry.CurrentUser.OpenSubKey(keyPath);
        return k != null;
    }

    public static (string Name, string Command, string? Icon, bool Shift)? Read(string keyPath)
    {
        using var k = Registry.CurrentUser.OpenSubKey(keyPath);
        if (k == null) return null;
        using var c = k.OpenSubKey("command");
        return (k.GetValue("MUIVerb") as string ?? "", c?.GetValue(null) as string ?? "", k.GetValue("Icon") as string, k.GetValue("Extended") != null);
    }

    public static void Delete(MenuEntry e)
    {
        using var root = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64);
        root.DeleteSubKeyTree(e.KeyPath, throwOnMissingSubKey: false);
        Native.NotifyShell();
    }
}
