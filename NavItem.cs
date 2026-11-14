using System.ComponentModel;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace DreddMenu;

public sealed class NavItem : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required SymbolRegular Symbol { get; init; }
    /// <summary>Раздел со списком пунктов меню (а не бэкапы/настройки).</summary>
    public bool IsEntries { get; init; }

    private string _title = "";
    public string Title
    {
        get => _title;
        set { _title = value; PropertyChanged?.Invoke(this, new(nameof(Title))); }
    }

    private string _count = "";
    public string Count
    {
        get => _count;
        set { _count = value; PropertyChanged?.Invoke(this, new(nameof(Count))); PropertyChanged?.Invoke(this, new(nameof(HasCount))); }
    }
    public bool HasCount => !string.IsNullOrEmpty(_count);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Строка в предпросмотре контекстного меню.</summary>
public sealed class PreviewItem
{
    public string Text { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsItem => !IsSeparator;
    /// <summary>Стандартный пункт Windows (рисуем приглушённо).</summary>
    public bool IsMuted { get; init; }
    /// <summary>Пункт, который изменился и ещё не применён.</summary>
    public bool IsHighlighted { get; init; }

    public static PreviewItem Separator => new() { IsSeparator = true };
}
