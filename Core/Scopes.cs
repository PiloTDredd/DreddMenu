namespace DreddMenu.Core;

/// <summary>Ветка реестра (относительно Software\Classes) и ключ строки для подписи.</summary>
public sealed record ScopeKey(string ClassPath, string LabelKey);

public sealed record Scope(string Id, ScopeKey[] Keys);

public static class Scopes
{
    public const string FileTypes = "filetypes";
    public const string Win11 = "win11";
    public const string TypePrefix = "type:";

    /// <summary>Классические разделы, которые сканируются сразу.</summary>
    public static readonly Scope[] Classic =
    {
        new("desktop", new[] { new ScopeKey("DesktopBackground", "Loc.DesktopBg"), new ScopeKey(@"Directory\Background", "Loc.FolderBg") }),
        new("files", new[] { new ScopeKey("*", "Loc.AllFiles"), new ScopeKey("AllFilesystemObjects", "Loc.FilesFolders") }),
        new("folders", new[] { new ScopeKey("Directory", "Loc.Folder"), new ScopeKey("Folder", "Loc.FolderShell") }),
        new("drives", new[] { new ScopeKey("Drive", "Loc.Drive") }),
    };

    /// <summary>Подпись для типа из манифеста пакета Windows 11.</summary>
    public static string ItemTypeLabel(string type) => type switch
    {
        "*" => Loc.T("Loc.AllFiles"),
        "Directory" => Loc.T("Loc.Folder"),
        @"Directory\Background" => Loc.T("Loc.FolderBg"),
        "Drive" => Loc.T("Loc.Drive"),
        _ => type,
    };
}
