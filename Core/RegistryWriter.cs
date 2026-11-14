using System.Diagnostics;
using Microsoft.Win32;

namespace DreddMenu.Core;

public static class RegistryWriter
{
    private const string ClassicMenuClsidKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    /// <summary>Вкл/выкл обычную команду через значение LegacyDisable (полностью обратимо).</summary>
    public static void SetVerbEnabled(RegistryHive hive, string keyPath, bool enabled)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(keyPath, writable: true)
                        ?? throw new InvalidOperationException($"Ключ не найден: {keyPath}");
        if (enabled) key.DeleteValue("LegacyDisable", throwOnMissingValue: false);
        else key.SetValue("LegacyDisable", "", RegistryValueKind.String);
    }

    /// <summary>Вкл/выкл shell-расширение через список Shell Extensions\Blocked (обратимо).</summary>
    public static void SetHandlerEnabled(string clsid, string name, bool enabled)
    {
        if (enabled)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var k = root.OpenSubKey(RegistryScanner.BlockedPath, writable: true);
                if (k == null) continue;
                foreach (var v in k.GetValueNames())
                    if (string.Equals(RegistryScanner.NormalizeClsid(v), clsid, StringComparison.OrdinalIgnoreCase))
                        k.DeleteValue(v, false);
            }
        }
        else
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var k = root.CreateSubKey(RegistryScanner.BlockedPath, writable: true);
            k.SetValue(clsid, name, RegistryValueKind.String);
        }
    }

    public static void Apply(IEnumerable<MenuEntry> changes)
    {
        var doneClsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in changes)
        {
            if (e.Kind == EntryKind.Verb)
                SetVerbEnabled(e.Hive, e.KeyPath, e.IsEnabled);
            else if (e.Clsid != null && doneClsids.Add(e.Clsid))
                SetHandlerEnabled(e.Clsid, e.Name, e.IsEnabled);
        }
        Native.NotifyShell();
    }

    // ---------- Меню Windows 11 ----------

    public static bool IsClassicMenuEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(ClassicMenuClsidKey + @"\InprocServer32");
        return k != null;
    }

    public static void SetClassicMenu(bool classic)
    {
        if (classic)
        {
            using var k = Registry.CurrentUser.CreateSubKey(ClassicMenuClsidKey + @"\InprocServer32", writable: true);
            k.SetValue("", "", RegistryValueKind.String);
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClassicMenuClsidKey, throwOnMissingSubKey: false);
        }
    }

    // ---------- Перезапуск проводника ----------

    public static async Task RestartExplorerAsync()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { }
        }
        // Обычно Windows сама поднимает проводник обратно, ждём.
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(250);
            if (Process.GetProcessesByName("explorer").Length > 0) return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }
}
