using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DreddMenu.Core;

public static class RegistryScanner
{
    public const string BlockedPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    private const string PackagesRepo = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>Понятные имена для расширений, у которых в реестре только техническое имя (RU, EN).</summary>
    private static readonly Dictionary<string, (string Ru, string En)> FriendlyHandlerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["New"] = ("Создать", "New"),
        ["Sharing"] = ("Общий доступ", "Give access to"),
        ["ModernSharing"] = ("Поделиться", "Share"),
        ["Library Location"] = ("Добавить в библиотеку", "Include in library"),
        ["SendTo"] = ("Отправить", "Send to"),
        ["CopyAsPathMenu"] = ("Копировать как путь", "Copy as path"),
        ["PintoStartScreen"] = ("Закрепить на начальном экране", "Pin to Start"),
        ["PintoHomeFile"] = ("Добавить в избранное", "Add to Favorites"),
        ["EPP"] = ("Проверка Microsoft Defender", "Scan with Microsoft Defender"),
        ["Open With"] = ("Открыть с помощью", "Open with"),
        ["Open With EncryptionMenu"] = ("Шифрование", "Encryption"),
        ["EncryptionMenu"] = ("Шифрование", "Encryption"),
        ["WorkFolders"] = ("Рабочие папки", "Work Folders"),
        ["DesktopSlideshow"] = ("Следующее фоновое изображение", "Next desktop background"),
        ["ShellImagePreview"] = ("Просмотр изображений", "Image preview"),
        ["EnhancedStorageShell"] = ("Расширенное хранилище", "Enhanced storage"),
        ["NvCplDesktopContext"] = ("Панель управления NVIDIA", "NVIDIA Control Panel"),
        ["igfxcui"] = ("Графика Intel", "Intel Graphics"),
        ["igfxDTCM"] = ("Графика Intel", "Intel Graphics"),
        ["ACE"] = ("AMD Radeon Software", "AMD Radeon Software"),
    };

    /// <summary>Команды, без которых тип файла может перестать открываться — помечаем системными.</summary>
    private static readonly HashSet<string> CriticalVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennew", "edit", "print", "printto", "play", "runas", "runasuser", "openas", "explore",
    };

    // ================== Классическое меню ==================

    public static List<MenuEntry> Scan()
    {
        var blocked = ReadBlocked();
        var result = new List<MenuEntry>();

        foreach (var scope in Scopes.Classic)
        foreach (var key in scope.Keys)
            ScanClass(key.ClassPath, scope.Id, Loc.T(key.LabelKey), blocked, result, markCritical: false);

        ScanPackaged(blocked, result);
        return Sort(result);
    }

    // ================== Типы файлов ==================

    public static string NormalizeExtension(string ext)
    {
        ext = ext.Trim().ToLowerInvariant();
        if (ext.StartsWith('*')) ext = ext[1..];
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    public static List<MenuEntry> ScanFileType(string ext)
    {
        ext = NormalizeExtension(ext);
        var blocked = ReadBlocked();
        var result = new List<MenuEntry>();
        var scopeId = Scopes.TypePrefix + ext;

        var classes = new List<string> { ext, $@"SystemFileAssociations\{ext}" };

        // Программа, назначенная пользователем, и программа по умолчанию.
        using (var uc = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice"))
            if (uc?.GetValue("ProgId") is string p && !string.IsNullOrWhiteSpace(p)) classes.Add(p);
        using (var extKey = Registry.ClassesRoot.OpenSubKey(ext))
        {
            if (extKey?.GetValue(null) is string progId && !string.IsNullOrWhiteSpace(progId)) classes.Add(progId);
            if (extKey?.GetValue("PerceivedType") is string perceived && !string.IsNullOrWhiteSpace(perceived))
                classes.Add($@"SystemFileAssociations\{perceived}");
        }

        foreach (var cls in classes.Distinct(StringComparer.OrdinalIgnoreCase))
            ScanClass(cls, scopeId, cls, blocked, result, markCritical: true);

        return Sort(result);
    }

    // ================== Общая часть ==================

    private static List<MenuEntry> Sort(List<MenuEntry> list) => list
        .OrderBy(e => e.IsSystem)
        .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static void ScanClass(string classPath, string scopeId, string label, HashSet<string> blocked,
        List<MenuEntry> result, bool markCritical)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            var classes = $@"Software\Classes\{classPath}";
            ScanVerbs(root, hive, $@"{classes}\shell", scopeId, label, result, markCritical);
            ScanHandlers(root, hive, $@"{classes}\shellex\ContextMenuHandlers", scopeId, label, blocked, result);
        }
    }

    private static void ScanVerbs(RegistryKey root, RegistryHive hive, string path, string scopeId, string label,
        List<MenuEntry> result, bool markCritical)
    {
        using var shell = TryOpen(root, path);
        if (shell == null) return;

        foreach (var name in shell.GetSubKeyNames())
        {
            using var verb = TryOpen(shell, name);
            if (verb == null) continue;
            // Такие пункты в меню вообще не показываются — нечего и переключать.
            if (verb.GetValue("ProgrammaticAccessOnly") != null) continue;

            var command = ReadDefault(verb, "command");
            var iconSpec = verb.GetValue("Icon") as string;
            var exe = IconLoader.ExeFromCommand(command);
            var exeExpanded = exe == null ? null : Environment.ExpandEnvironmentVariables(exe);

            var title = Native.LoadIndirect(verb.GetValue("MUIVerb") as string)
                        ?? Native.LoadIndirect(verb.GetValue(null) as string);
            title = CleanTitle(title);
            if (string.IsNullOrWhiteSpace(title)) title = name;

            bool isSystem = exe != null
                ? IconLoader.IsInWindowsDir(exe)
                : iconSpec == null || IconLoader.IsInWindowsDir(IconLoader.ParseIconSpec(iconSpec).path);
            if (markCritical && CriticalVerbs.Contains(name)) isSystem = true;

            // Битый: путь к программе указан полностью, но файла нет.
            bool isBroken = exeExpanded != null && Path.IsPathRooted(exeExpanded) && !File.Exists(exeExpanded);

            var entry = new MenuEntry
            {
                Name = title,
                ScopeId = scopeId,
                LocationLabel = label,
                Kind = EntryKind.Verb,
                Hive = hive,
                KeyPath = $@"{path}\{name}",
                Details = command ?? (verb.GetValue("SubCommands") != null || verb.GetValue("ExtendedSubCommandsKey") != null
                    ? Loc.T("Details.Submenu")
                    : verb.GetValue("DelegateExecute") is string de ? $"DelegateExecute {de}" : null),
                TargetPath = exeExpanded,
                IsExtended = verb.GetValue("Extended") != null,
                IsSystem = isSystem,
                IsCustom = name.StartsWith(CustomItems.Prefix, StringComparison.OrdinalIgnoreCase),
                IsBroken = isBroken,
                Icon = IconLoader.Load(iconSpec) ?? IconLoader.Load(exe),
                OriginalEnabled = verb.GetValue("LegacyDisable") == null,
            };
            entry.IsEnabled = entry.OriginalEnabled;
            result.Add(entry);
        }
    }

    private static void ScanHandlers(RegistryKey root, RegistryHive hive, string path, string scopeId, string label,
        HashSet<string> blocked, List<MenuEntry> result)
    {
        using var handlers = TryOpen(root, path);
        if (handlers == null) return;

        foreach (var name in handlers.GetSubKeyNames())
        {
            using var h = TryOpen(handlers, name);
            if (h == null) continue;

            var clsid = NormalizeClsid(h.GetValue(null) as string) ?? NormalizeClsid(name);
            if (clsid == null) continue;

            string? dll = null, clsidName = null;
            bool clsidExists;
            using (var clsidKey = TryOpen(Registry.ClassesRoot, $@"CLSID\{clsid}"))
            {
                clsidExists = clsidKey != null;
                clsidName = clsidKey?.GetValue(null) as string;
                dll = ReadDefault(clsidKey, "InprocServer32");
            }
            var dllExpanded = dll == null ? null : Environment.ExpandEnvironmentVariables(dll);

            string title;
            if (FriendlyHandlerNames.TryGetValue(name.Trim(), out var friendly))
                title = Loc.Current == "en" ? friendly.En : friendly.Ru;
            else if (NormalizeClsid(name) == null) title = name.Trim();
            else if (!string.IsNullOrWhiteSpace(clsidName)) title = clsidName!;
            else title = dll != null ? Path.GetFileNameWithoutExtension(dll) : clsid;

            bool isBroken = !clsidExists
                            || (dllExpanded != null && Path.IsPathRooted(dllExpanded) && !File.Exists(dllExpanded));

            var entry = new MenuEntry
            {
                Name = title,
                ScopeId = scopeId,
                LocationLabel = label,
                Kind = EntryKind.Handler,
                Hive = hive,
                KeyPath = $@"{path}\{name}",
                Clsid = clsid,
                Details = dllExpanded != null ? $"{clsid}  ·  {dllExpanded}" : clsid,
                TargetPath = dllExpanded,
                IsSystem = dll == null ? clsidExists : IconLoader.IsInWindowsDir(dll),
                IsBroken = isBroken,
                Icon = IconLoader.Load(dll),
                OriginalEnabled = !blocked.Contains(clsid),
            };
            entry.IsEnabled = entry.OriginalEnabled;
            result.Add(entry);
        }
    }

    // ================== Пункты нового меню Windows 11 (приложения-пакеты) ==================

    private static void ScanPackaged(HashSet<string> blocked, List<MenuEntry> result)
    {
        using var repo = TryOpen(Registry.CurrentUser, PackagesRepo);
        if (repo == null) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fullName in repo.GetSubKeyNames())
        {
            try
            {
                using var pk = TryOpen(repo, fullName);
                if (pk?.GetValue("PackageRootFolder") is not string rootFolder) continue;
                var manifestPath = Path.Combine(rootFolder, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) continue;
                var text = File.ReadAllText(manifestPath);
                if (!text.Contains("FileExplorerContextMenus")) continue;

                var doc = XDocument.Parse(text);
                var rootEl = doc.Root!;
                var identity = rootEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name")?.Value ?? fullName;
                var props = rootEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Properties");
                var displayRaw = props?.Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value;
                var logo = props?.Elements().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;
                var displayName = ResolvePackageString(displayRaw, fullName, identity) ?? identity;
                var icon = LoadPackageLogo(rootFolder, logo);

                var verbs = doc.Descendants()
                    .Where(e => e.Name.LocalName == "ItemType" && e.Ancestors().Any(a => a.Name.LocalName == "FileExplorerContextMenus"))
                    .SelectMany(it => it.Elements().Where(v => v.Name.LocalName == "Verb")
                        .Select(v => (Type: it.Attribute("Type")?.Value ?? "*", Id: v.Attribute("Id")?.Value ?? "", Clsid: NormalizeClsid(v.Attribute("Clsid")?.Value))))
                    .Where(v => v.Clsid != null)
                    .GroupBy(v => v.Clsid!);

                foreach (var g in verbs)
                {
                    if (!seen.Add(g.Key)) continue;
                    var entry = new MenuEntry
                    {
                        Name = Humanize(g.First().Id),
                        ScopeId = Scopes.Win11,
                        LocationLabel = string.Join(", ", g.Select(v => Scopes.ItemTypeLabel(v.Type)).Distinct()),
                        Kind = EntryKind.Handler,
                        Hive = RegistryHive.CurrentUser,
                        KeyPath = "package:" + fullName,
                        Clsid = g.Key,
                        PackageName = displayName,
                        Details = $"{g.Key}  ·  {fullName}",
                        IsSystem = identity.StartsWith("Microsoft.Windows", StringComparison.OrdinalIgnoreCase),
                        Icon = icon,
                        OriginalEnabled = !blocked.Contains(g.Key),
                    };
                    entry.IsEnabled = entry.OriginalEnabled;
                    result.Add(entry);
                }
            }
            catch { /* кривой манифест — пропускаем */ }
        }
    }

    /// <summary>"ms-resource:AppName" → нормальный текст из ресурсов пакета.</summary>
    private static string? ResolvePackageString(string? raw, string fullName, string identity)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!raw.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return raw;
        var res = raw["ms-resource:".Length..];
        string uri = res.StartsWith("//") ? "ms-resource:" + res
            : res.StartsWith('/') ? $"ms-resource://{identity}{res}"
            : res.Contains('/') ? $"ms-resource://{identity}/{res}"
            : $"ms-resource://{identity}/Resources/{res}";
        return Native.LoadIndirect($"@{{{fullName}?{uri}}}");
    }

    private static ImageSource? LoadPackageLogo(string root, string? logo)
    {
        if (string.IsNullOrWhiteSpace(logo)) return null;
        try
        {
            var path = Path.Combine(root, logo.Replace('/', '\\'));
            if (!File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path)!;
                var stem = Path.GetFileNameWithoutExtension(path);
                if (!Directory.Exists(dir)) return null;
                var candidates = Directory.GetFiles(dir, stem + "*" + Path.GetExtension(path));
                path = candidates.FirstOrDefault(c => c.Contains("scale-200")) ?? candidates.FirstOrDefault(c => c.Contains("scale-100"))
                       ?? candidates.FirstOrDefault() ?? "";
                if (!File.Exists(path)) return null;
            }
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = 48;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    /// <summary>"OpenProjectInCodex" → "Open Project In Codex".</summary>
    private static string Humanize(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            if (i > 0 && char.IsUpper(id[i]) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(id[i] == '_' ? ' ' : id[i]);
        }
        return sb.ToString();
    }

    // ================== Утилиты ==================

    public static HashSet<string> ReadBlocked()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = TryOpen(root, BlockedPath);
            if (k == null) continue;
            foreach (var v in k.GetValueNames())
                if (NormalizeClsid(v) is { } c) set.Add(c);
        }
        return set;
    }

    public static string? NormalizeClsid(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return Guid.TryParse(s.Trim(), out var g) ? g.ToString("B").ToUpperInvariant() : null;
    }

    private static string? CleanTitle(string? s)
    {
        if (s == null) return null;
        return s.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
    }

    private static string? ReadDefault(RegistryKey? parent, string sub)
    {
        if (parent == null) return null;
        using var k = TryOpen(parent, sub);
        return k?.GetValue(null) as string;
    }

    private static RegistryKey? TryOpen(RegistryKey parent, string path)
    {
        try { return parent.OpenSubKey(path, false); }
        catch { return null; }
    }
}
