using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DreddMenu.Core;

internal static class IconLoader
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string Sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

    /// <summary>Иконка по строке вида "C:\path\app.exe,0" или "imageres.dll,-5303".</summary>
    public static ImageSource? Load(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(spec, out var cached)) return cached;
        }

        var (path, index) = ParseIconSpec(spec);
        ImageSource? result = null;
        var full = ResolvePath(path);
        if (full != null)
        {
            try
            {
                var large = new IntPtr[1];
                if (Native.ExtractIconEx(full, index, large, null, 1) > 0 && large[0] != IntPtr.Zero)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    Native.DestroyIcon(large[0]);
                    result = src;
                }
            }
            catch { /* битая иконка — просто без неё */ }
        }

        lock (Cache) Cache[spec] = result;
        return result;
    }

    public static (string path, int index) ParseIconSpec(string spec)
    {
        spec = Environment.ExpandEnvironmentVariables(spec.Trim());
        if (spec.StartsWith('"'))
        {
            int end = spec.IndexOf('"', 1);
            if (end > 1)
            {
                var p = spec[1..end];
                var rest = spec[(end + 1)..].Trim().TrimStart(',').Trim();
                return (p, int.TryParse(rest, out var i) ? i : 0);
            }
        }
        int comma = spec.LastIndexOf(',');
        if (comma > 0 && int.TryParse(spec[(comma + 1)..].Trim(), out var idx))
            return (spec[..comma].Trim().Trim('"'), idx);
        return (spec.Trim('"'), 0);
    }

    /// <summary>Достаёт путь к exe из строки команды.</summary>
    public static string? ExeFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            return end > 1 ? cmd[1..end] : null;
        }
        int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return cmd[..(exe + 4)];
        int sp = cmd.IndexOf(' ');
        return sp > 0 ? cmd[..sp] : cmd;
    }

    public static string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try
        {
            if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
            var dirs = new[] { Sys32, WinDir }
                .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
            foreach (var dir in dirs)
            {
                try
                {
                    var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), path);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    public static bool IsInWindowsDir(string? path)
    {
        var full = ResolvePath(path);
        // Драйверы (NVIDIA, Intel, AMD) лежат в DriverStore внутри Windows, но это не сама Windows.
        return full != null
               && full.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase)
               && !full.Contains(@"\DriverStore\", StringComparison.OrdinalIgnoreCase);
    }
}
