using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using DreddMenu.Core;

namespace DreddMenu;

public partial class App : Application
{
    public static bool IsAdmin { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        Loc.Apply(AppSettings.Current.Language);

        // Служебный режим: выгрузить найденные пункты в текстовый файл (ничего не меняет).
        int dump = Array.IndexOf(e.Args, "--dump");
        if (dump >= 0 && dump + 1 < e.Args.Length)
        {
            var list = RegistryScanner.Scan();
            int t = Array.IndexOf(e.Args, "--type");
            if (t >= 0 && t + 1 < e.Args.Length) list.AddRange(RegistryScanner.ScanFileType(e.Args[t + 1]));
            var lines = list.Select(x =>
                $"{x.ScopeId,-10} | {x.HiveLabel} | {x.KindLabel,-10} | {(x.OriginalEnabled ? "ON " : "OFF")} | {(x.IsSystem ? "sys" : "   ")} | {(x.IsBroken ? "BROKEN" : "      ")} | {(x.HasIcon ? "ico" : "   ")} | {x.Name} | {x.LocationLabel} | {x.KeyPath} | {x.Details}");
            File.WriteAllLines(e.Args[dump + 1], lines);
            Shutdown();
            return;
        }

        // Для записи в HKLM нужны права администратора — перезапускаемся с ними.
        if (!IsAdmin && !e.Args.Contains("--no-elevate"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", e.Args.Select(a => $"\"{a}\"")),
                });
                Shutdown();
                return;
            }
            catch (Win32Exception)
            {
                // Пользователь нажал «Нет» в UAC — работаем в режиме просмотра.
            }
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>Неожиданная ошибка — пишем лог и показываем сообщение, а не молча падаем.</summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            File.AppendAllText(Path.Combine(AppInfo.DataFolder, "error.log"), $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch { }
        MessageBox.Show(e.Exception.Message, $"{AppInfo.Name} — {Loc.T("T.Error")}", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
