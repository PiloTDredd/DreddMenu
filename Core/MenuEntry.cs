using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace DreddMenu.Core;

public enum EntryKind
{
    /// <summary>Обычная команда (ключ shell\...).</summary>
    Verb,
    /// <summary>Shell-расширение, COM-обработчик (shellex\ContextMenuHandlers\... или пакет Windows 11).</summary>
    Handler,
}

public sealed class MenuEntry : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string ScopeId { get; init; }
    public required string LocationLabel { get; init; }
    public required EntryKind Kind { get; init; }
    public required RegistryHive Hive { get; init; }
    /// <summary>Путь ключа внутри улья (или "package:ИмяПакета" для приложений Windows 11).</summary>
    public required string KeyPath { get; init; }
    public string KeyName => KeyPath[(KeyPath.LastIndexOf('\\') + 1)..];
    public string? Clsid { get; init; }
    public string? Details { get; init; }
    /// <summary>Путь к программе, которую запускает пункт (для поиска битых пунктов).</summary>
    public string? TargetPath { get; init; }
    public bool IsExtended { get; init; }
    public bool IsSystem { get; init; }
    public bool IsCustom { get; init; }
    public bool IsBroken { get; init; }
    public string? PackageName { get; init; }
    public bool IsPackaged => PackageName != null;
    public ImageSource? Icon { get; init; }
    public bool OriginalEnabled { get; set; }

    public bool HasIcon => Icon != null;
    public bool NoIcon => Icon == null;
    public SymbolRegular FallbackSymbol => IsPackaged ? SymbolRegular.Apps24
        : Kind == EntryKind.Handler ? SymbolRegular.PuzzlePiece24 : SymbolRegular.Cursor24;

    public string KindLabel => IsPackaged ? Loc.T("Kind.App")
        : Kind == EntryKind.Verb ? Loc.T("Kind.Verb") : Loc.T("Kind.Handler");

    public string HiveLabel => Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";

    public string Subtitle => IsPackaged
        ? $"{LocationLabel} · {PackageName}"
        : $"{LocationLabel} · {HiveLabel} · {KeyName}";

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsDirty => IsEnabled != OriginalEnabled;

    public void Reset() => IsEnabled = OriginalEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
