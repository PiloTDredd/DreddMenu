using System.Diagnostics;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace DreddMenu.Core;

public enum CheckStatus { Ok, Warning, Error }

public sealed class CheckResult
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required CheckStatus Status { get; init; }

    public SymbolRegular Symbol => Status switch
    {
        CheckStatus.Ok => SymbolRegular.CheckmarkCircle24,
        CheckStatus.Warning => SymbolRegular.Warning24,
        _ => SymbolRegular.ErrorCircle24,
    };

    public bool IsOk => Status == CheckStatus.Ok;
    public bool IsWarning => Status == CheckStatus.Warning;
    public bool IsError => Status == CheckStatus.Error;
}

/// <summary>Проверка, что всё нужное для работы есть и доступно.</summary>
public static class SelfCheck
{
    public static List<CheckResult> Run(IReadOnlyCollection<MenuEntry> entries)
    {
        var r = new List<CheckResult>();

        // 1. Версия Windows
        var build = Environment.OSVersion.Version.Build;
        r.Add(build >= 22000
            ? Ok("Check.Os", Loc.F("Check.Os.Win11", build))
            : build >= 17763
                ? Ok("Check.Os", Loc.F("Check.Os.Win10", build))
                : Warn("Check.Os", Loc.F("Check.Os.Old", build)));

        // 2. Права администратора
        r.Add(App.IsAdmin ? Ok("Check.Admin", Loc.T("Check.Admin.Ok")) : Warn("Check.Admin", Loc.T("Check.Admin.No")));

        // 3. Чтение реестра
        r.Add(Try(() =>
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"Software\Classes\Directory\Background\shell");
            if (k == null) throw new Exception();
        }) ? Ok("Check.RegRead", Loc.T("Check.RegRead.Ok")) : Fail("Check.RegRead", Loc.T("Check.RegRead.Fail")));

        // 4. Запись в реестр (только открываем на запись — ничего не меняем)
        bool hkcu = Try(() => { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true) ?? throw new Exception(); });
        bool hklm = App.IsAdmin && Try(() =>
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"Software\Classes\Directory\Background\shell", true) ?? throw new Exception();
        });
        r.Add(hkcu && hklm ? Ok("Check.RegWrite", Loc.T("Check.RegWrite.Ok"))
            : hkcu ? Warn("Check.RegWrite", Loc.T("Check.RegWrite.UserOnly"))
            : Fail("Check.RegWrite", Loc.T("Check.RegWrite.Fail")));

        // 5. Папка для настроек и бэкапов
        r.Add(Try(() =>
        {
            Directory.CreateDirectory(BackupStore.Folder);
            var test = Path.Combine(BackupStore.Folder, ".write_test");
            File.WriteAllText(test, "ok");
            File.Delete(test);
        }) ? Ok("Check.Data", AppInfo.DataFolder) : Fail("Check.Data", Loc.F("Check.Data.Fail", AppInfo.DataFolder)));

        // 6. Целостность бэкапов
        var (total, broken) = BackupStore.CheckFiles();
        r.Add(broken == 0
            ? Ok("Check.Backups", Loc.F("Check.Backups.Ok", total))
            : Warn("Check.Backups", Loc.F("Check.Backups.Broken", broken)));

        // 7. Проводник
        r.Add(Process.GetProcessesByName("explorer").Length > 0
            ? Ok("Check.Explorer", Loc.T("Check.Explorer.Ok"))
            : Warn("Check.Explorer", Loc.T("Check.Explorer.No")));

        // 8. Битые пункты меню
        var brokenEntries = entries.Where(e => e.IsBroken).Select(e => e.Name).Distinct().ToList();
        r.Add(brokenEntries.Count == 0
            ? Ok("Check.Broken", Loc.T("Check.Broken.Ok"))
            : Warn("Check.Broken", Loc.F("Check.Broken.Found", brokenEntries.Count, string.Join(", ", brokenEntries.Take(5)))));

        return r;
    }

    private static bool Try(Action a)
    {
        try { a(); return true; } catch { return false; }
    }

    private static CheckResult Ok(string key, string detail) => new() { Title = Loc.T(key), Detail = detail, Status = CheckStatus.Ok };
    private static CheckResult Warn(string key, string detail) => new() { Title = Loc.T(key), Detail = detail, Status = CheckStatus.Warning };
    private static CheckResult Fail(string key, string detail) => new() { Title = Loc.T(key), Detail = detail, Status = CheckStatus.Error };
}
