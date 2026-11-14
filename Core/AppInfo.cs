using System.Reflection;

namespace DreddMenu.Core;

public static class AppInfo
{
    /// <summary>Ссылка на поддержку проекта. Пустая — кнопка скрыта.</summary>
    public const string DonateUrl = "https://pay.cloudtips.ru/p/129557e1";

    /// <summary>Ссылка на репозиторий. Пустая — кнопка скрыта.</summary>
    public const string GitHubUrl = "https://github.com/PiloTDredd/DreddMenu";

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DreddMenu");

    public const string Name = "Dredd Menu";
    public const string Author = "PiloTDredd";
}
