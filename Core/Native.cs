using System.Runtime.InteropServices;
using System.Text;

namespace DreddMenu.Core;

internal static class Native
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public const int SHCNE_ASSOCCHANGED = 0x08000000;

    /// <summary>"@shell32.dll,-1234" → нормальный текст.</summary>
    public static string? LoadIndirect(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return source;
        if (!source.StartsWith('@')) return source;
        var sb = new StringBuilder(1024);
        var expanded = "@" + Environment.ExpandEnvironmentVariables(source[1..]);
        return SHLoadIndirectString(expanded, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
    }

    /// <summary>Уведомить оболочку, что ассоциации/меню поменялись.</summary>
    public static void NotifyShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
