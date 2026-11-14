using System.Globalization;
using System.Windows;

namespace DreddMenu.Core;

/// <summary>Переключение языка RU/EN. Строки лежат в Lang/ru.xaml и Lang/en.xaml.</summary>
public static class Loc
{
    private static Dictionary<string, string> _strings = new();

    public static string Current { get; private set; } = "ru";

    public static event Action? Changed;

    public static string SystemDefault =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? "ru" : "en";

    public static void Apply(string? lang)
    {
        lang = lang is "en" or "ru" ? lang : SystemDefault;

        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Lang/{lang}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        var old = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Lang/") == true);
        if (old != null) merged.Remove(old);
        merged.Add(dict);

        _strings = dict.Keys.OfType<string>().ToDictionary(k => k, k => dict[k] as string ?? k);
        Current = lang;
        Changed?.Invoke();
    }

    public static string T(string key) => _strings.TryGetValue(key, out var v) ? v : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);
}
