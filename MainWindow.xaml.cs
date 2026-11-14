using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DreddMenu.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace DreddMenu;

public partial class MainWindow : FluentWindow
{
    private List<MenuEntry> _all = new();
    private ICollectionView? _view;
    private readonly ObservableCollection<NavItem> _nav = new();
    private readonly List<string> _scannedTypes = new();
    private string _navId = "desktop";
    private string _scopeId = "desktop";
    private bool _syncing, _initializing = true, _loading, _watchingTheme;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    private static readonly string[] CommonExtensions =
        { ".txt", ".png", ".jpg", ".mp4", ".mp3", ".zip", ".rar", ".pdf", ".docx", ".exe", ".lnk", ".py" };

    private static readonly string[] DevKeywords =
    {
        "git", "code", "pycharm", "idea", "rider", "webstorm", "clion", "android studio", "visual studio", "sublime",
        "notepad++", "terminal", "powershell", "cmd", "команд", "wsl", "linux", "bash", "cursor", "zed",
    };

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(AppSettings.Current.Theme);

        var navDefs = new (string Id, SymbolRegular Symbol, bool Entries)[]
        {
            ("desktop", SymbolRegular.Desktop24, true),
            ("files", SymbolRegular.Document24, true),
            ("folders", SymbolRegular.Folder24, true),
            ("drives", SymbolRegular.Storage24, true),
            (Scopes.FileTypes, SymbolRegular.DocumentBulletList24, true),
            (Scopes.Win11, SymbolRegular.Apps24, true),
            ("backups", SymbolRegular.History24, false),
            ("settings", SymbolRegular.Settings24, false),
        };
        foreach (var d in navDefs) _nav.Add(new NavItem { Id = d.Id, Symbol = d.Symbol, IsEntries = d.Entries });
        NavList.ItemsSource = _nav;
        ExtChips.ItemsSource = CommonExtensions;

        LangCombo.SelectedIndex = Loc.Current == "en" ? 1 : 0;
        ThemeCombo.SelectedIndex = AppSettings.Current.Theme switch { "dark" => 1, "light" => 2, _ => 0 };
        PreviewToggle.IsChecked = AppSettings.Current.ShowPreview;
        VersionText.Text = "v" + AppInfo.Version;
        DonateButton.Visibility = string.IsNullOrEmpty(AppInfo.DonateUrl) ? Visibility.Collapsed : Visibility.Visible;
        GitHubButton.Visibility = string.IsNullOrEmpty(AppInfo.GitHubUrl) ? Visibility.Collapsed : Visibility.Visible;
        AdminIcon.Symbol = App.IsAdmin ? SymbolRegular.ShieldCheckmark24 : SymbolRegular.ShieldError24;
        ApplyTexts();

        Loc.Changed += OnLanguageChanged;
        SizeChanged += (_, _) => UpdatePreviewVisibility();
        _toastTimer.Tick += (_, _) => { Toast.IsOpen = false; _toastTimer.Stop(); };
        Loaded += OnLoaded;
        Closing += OnClosing;
        _initializing = false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavList.SelectedIndex = 0;
        await ReloadAsync();

        if (await TryDebugScreenshotAsync()) return;

        // Проверка системы: всегда при первом запуске, дальше — только если есть ошибки.
        var results = SelfCheck.Run(_all);
        if (!AppSettings.Current.FirstRunDone || results.Any(r => r.IsError))
        {
            ShowCheck(results);
            AppSettings.Current.FirstRunDone = true;
            AppSettings.Current.Save();
        }
    }

    // ======================= Тексты и язык =======================

    private void ApplyTexts()
    {
        foreach (var n in _nav) n.Title = Loc.T($"Nav.{n.Id}");
        AdminText.Text = Loc.T(App.IsAdmin ? "Admin.Yes" : "Admin.No");
        UpdatePageHeader();
        UpdatePending();
    }

    private async void OnLanguageChanged()
    {
        ApplyTexts();
        // Подписи пунктов собираются при сканировании — пересканируем, сохранив несохранённые тумблеры.
        var dirty = _all.Where(e => e.IsDirty).Select(e => (e.Kind, e.Hive, e.KeyPath, e.Clsid, e.IsEnabled)).ToList();
        await ReloadAsync();
        foreach (var d in dirty)
        foreach (var e in _all.Where(e => e.Kind == d.Kind && e.Hive == d.Hive && e.KeyPath == d.KeyPath))
            e.IsEnabled = d.IsEnabled;
        if (NavList.SelectedItem is NavItem { Id: "backups" }) LoadBackups();
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LangCombo.SelectedItem is not ComboBoxItem { Tag: string lang }) return;
        AppSettings.Current.Language = lang;
        AppSettings.Current.Save();
        Loc.Apply(lang);
    }

    // ======================= Тема =======================

    private void ApplyTheme(string theme)
    {
        if (theme == "system")
        {
            ApplicationThemeManager.ApplySystemTheme();
            if (!_watchingTheme)
            {
                SystemThemeWatcher.Watch(this);
                _watchingTheme = true;
            }
        }
        else
        {
            // Отписываться можно только от уже загруженного окна, которое мы реально слушаем.
            if (_watchingTheme && IsLoaded)
            {
                try { SystemThemeWatcher.UnWatch(this); } catch { }
                _watchingTheme = false;
            }
            ApplicationThemeManager.Apply(theme == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark, WindowBackdropType.Mica);
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        AppSettings.Current.Theme = theme;
        AppSettings.Current.Save();
        ApplyTheme(theme);
    }

    // ======================= Загрузка =======================

    private async Task ReloadAsync()
    {
        _loading = true;
        LoadingRing.Visibility = Visibility.Visible;
        EntriesScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        try
        {
            foreach (var e in _all) e.PropertyChanged -= Entry_PropertyChanged;
            var types = _scannedTypes.ToList();
            _all = await Task.Run(() =>
            {
                var list = RegistryScanner.Scan();
                foreach (var t in types) list.AddRange(RegistryScanner.ScanFileType(t));
                return list;
            });
            foreach (var e in _all) e.PropertyChanged += Entry_PropertyChanged;

            _view = CollectionViewSource.GetDefaultView(_all);
            _view.Filter = FilterEntry;
            EntriesList.ItemsSource = _view;
            UpdateCounts();
            UpdatePending();
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), Loc.T("T.ReadFail") + " " + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _loading = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            EntriesScroll.Visibility = Visibility.Visible;
            ApplyFilter();
        }
    }

    private bool FilterEntry(object o)
    {
        if (o is not MenuEntry e || e.ScopeId != _scopeId) return false;
        // Системные прячем, но выключенные или изменённые показываем всегда — чтобы их можно было вернуть.
        if (e.IsSystem && ShowSystemBox.IsChecked != true && e.OriginalEnabled && !e.IsDirty) return false;
        var q = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || e.KeyPath.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (e.Details?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ApplyFilter()
    {
        _view?.Refresh();
        bool empty = _view == null || _view.IsEmpty;
        EmptyText.Text = _navId == Scopes.FileTypes && !_scopeId.StartsWith(Scopes.TypePrefix)
            ? Loc.T("FileTypes.Hint")
            : _navId == Scopes.Win11 ? Loc.T("Empty.Win11") : Loc.T("Empty.Nothing");
        EmptyState.Visibility = empty && !_loading ? Visibility.Visible : Visibility.Collapsed;
        RebuildPreview();
    }

    private void UpdateCounts()
    {
        foreach (var n in _nav.Where(n => n.IsEntries && n.Id != Scopes.FileTypes))
        {
            var count = _all.Count(e => e.ScopeId == n.Id && !e.IsSystem);
            n.Count = count > 0 ? count.ToString() : "";
        }
    }

    private void UpdatePageHeader()
    {
        if (!_nav.Any()) return;
        var nav = _nav.FirstOrDefault(n => n.Id == _navId);
        if (nav == null || !nav.IsEntries) return;
        PageTitle.Text = _scopeId.StartsWith(Scopes.TypePrefix)
            ? $"{nav.Title}  ·  {_scopeId[Scopes.TypePrefix.Length..]}"
            : nav.Title;
        PageSubtitle.Text = Loc.T($"Page.{_navId}.Desc");
    }

    // ======================= Изменения =======================

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MenuEntry.IsEnabled) || sender is not MenuEntry changed) return;

        // Расширение блокируется по CLSID сразу везде — синхронизируем все его копии.
        if (!_syncing && changed.Kind == EntryKind.Handler && changed.Clsid != null)
        {
            _syncing = true;
            foreach (var other in _all.Where(o => o != changed && o.Kind == EntryKind.Handler && o.Clsid == changed.Clsid))
                other.IsEnabled = changed.IsEnabled;
            _syncing = false;
        }
        UpdatePending();
        RebuildPreview();
    }

    private IEnumerable<MenuEntry> DirtyEntries() => _all.Where(e => e.IsDirty);

    private void UpdatePending()
    {
        int n = DirtyEntries().Count();
        PendingBar.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingText.Text = Loc.F("Pending.Text", n);
    }

    private void ApplyChanges(List<MenuEntry> changes)
    {
        BackupStore.Save(changes);
        RegistryWriter.Apply(changes);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var changes = DirtyEntries().ToList();
        if (changes.Count == 0) return;
        try
        {
            ApplyChanges(changes);
            bool anyHandler = changes.Any(c => c.Kind == EntryKind.Handler);
            await ReloadAsync();
            ShowToast(Loc.T("T.Done"), Loc.F(anyHandler ? "T.AppliedRestart" : "T.Applied", changes.Count), InfoBarSeverity.Success);
        }
        catch (UnauthorizedAccessException)
        {
            ShowToast(Loc.T("T.NoAccess"), Loc.T("T.NoAccessMsg"), InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in DirtyEntries().ToList()) entry.Reset();
        ApplyFilter();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!DirtyEntries().Any()) return;
        var r = System.Windows.MessageBox.Show(this, Loc.T("D.UnsavedExit"), AppInfo.Name,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (r != MessageBoxResult.Yes) return;
        try { ApplyChanges(DirtyEntries().ToList()); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, Loc.T("T.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            e.Cancel = true;
        }
    }

    // ======================= Навигация и фильтры =======================

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem item) return;
        _navId = item.Id;

        EntriesPanel.Visibility = item.IsEntries ? Visibility.Visible : Visibility.Collapsed;
        BackupsPanel.Visibility = item.Id == "backups" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = item.Id == "settings" ? Visibility.Visible : Visibility.Collapsed;
        Win11Card.Visibility = item.Id == Scopes.Win11 ? Visibility.Visible : Visibility.Collapsed;
        FileTypeBar.Visibility = item.Id == Scopes.FileTypes ? Visibility.Visible : Visibility.Collapsed;
        AddButton.Visibility = item.Id is Scopes.Win11 or Scopes.FileTypes ? Visibility.Collapsed : Visibility.Visible;

        if (item.IsEntries)
        {
            if (item.Id == Scopes.FileTypes)
                _scopeId = _scannedTypes.Count > 0 ? Scopes.TypePrefix + _scannedTypes[^1] : "";
            else
                _scopeId = item.Id;
            if (item.Id == Scopes.Win11) ClassicToggle.IsChecked = RegistryWriter.IsClassicMenuEnabled();
            UpdatePageHeader();
            UpdatePreviewVisibility();
            ApplyFilter();
            EntriesScroll.ScrollToTop();
        }
        else if (item.Id == "backups")
        {
            LoadBackups();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DirtyEntries().Any())
        {
            var r = System.Windows.MessageBox.Show(this, Loc.T("D.UnsavedRefresh"), AppInfo.Name,
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
        }
        await ReloadAsync();
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RegistryWriter.RestartExplorerAsync();
            ShowToast(Loc.T("T.ExplorerRestarted"), Loc.T("T.ExplorerRestartedMsg"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), ex.Message, InfoBarSeverity.Error);
        }
    }

    // ======================= Типы файлов =======================

    private void ExtBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ShowFileType(ExtBox.Text);
    }

    private void ShowFileType_Click(object sender, RoutedEventArgs e) => ShowFileType(ExtBox.Text);

    private void ExtChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string ext) ShowFileType(ext);
    }

    private async void ShowFileType(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext) || ext.Trim().Trim('.', '*').Length == 0) return;
        ext = RegistryScanner.NormalizeExtension(ext);
        ExtBox.Text = ext;
        _scopeId = Scopes.TypePrefix + ext;

        if (!_scannedTypes.Contains(ext))
        {
            _scannedTypes.Add(ext);
            _loading = true;
            LoadingRing.Visibility = Visibility.Visible;
            var found = await Task.Run(() => RegistryScanner.ScanFileType(ext));
            foreach (var f in found) f.PropertyChanged += Entry_PropertyChanged;
            _all.AddRange(found);
            _loading = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
        UpdatePageHeader();
        UpdatePreviewVisibility();
        ApplyFilter();
    }

    // ======================= Windows 11 =======================

    private void ClassicToggle_Click(object sender, RoutedEventArgs e)
    {
        bool want = ClassicToggle.IsChecked == true;
        try
        {
            RegistryWriter.SetClassicMenu(want);
            ShowToast(Loc.T(want ? "T.ClassicOn" : "T.ClassicOff"), Loc.T("T.ClassicMsg"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ClassicToggle.IsChecked = !want;
            ShowToast(Loc.T("T.Error"), ex.Message, InfoBarSeverity.Error);
        }
    }

    // ======================= Свои пункты =======================

    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var preselect = _navId switch
        {
            "desktop" => "background",
            "files" => "files",
            "folders" => "folders",
            "drives" => "drives",
            _ => "background",
        };
        var dlg = new AddItemWindow(preselect) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await ReloadAsync();
        ShowToast(Loc.T("T.ItemAdded"), Loc.F("T.ItemAddedMsg", dlg.CreatedName), InfoBarSeverity.Success);
    }

    private async void DeleteCustom_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MenuEntry entry) return;
        var r = System.Windows.MessageBox.Show(this, Loc.F("D.DeleteItemQ", entry.Name), Loc.T("D.DeleteItemTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        try
        {
            CustomItems.Delete(entry);
            await ReloadAsync();
            ShowToast(Loc.T("T.Done"), Loc.F("T.ItemDeleted", entry.Name), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), ex.Message, InfoBarSeverity.Error);
        }
    }

    // ======================= Наборы =======================

    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        var menu = PresetsButton.ContextMenu!;
        menu.PlacementTarget = PresetsButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void RunPreset(Func<MenuEntry, bool?> decide)
    {
        int n = 0;
        foreach (var entry in _all.Where(x => !x.ScopeId.StartsWith(Scopes.TypePrefix)))
        {
            var want = decide(entry);
            if (want == null || want == entry.IsEnabled) continue;
            entry.IsEnabled = want.Value;
            n++;
        }
        ApplyFilter();
        ShowToast(Loc.T("Btn.Presets"), n == 0 ? Loc.T("Preset.Nothing") : Loc.F("Preset.Done", n), InfoBarSeverity.Informational);
    }

    private void PresetClean_Click(object sender, RoutedEventArgs e) =>
        RunPreset(x => x.IsSystem || x.IsCustom ? null : false);

    private void PresetDev_Click(object sender, RoutedEventArgs e) =>
        RunPreset(x =>
        {
            if (x.IsSystem || x.IsCustom) return null;
            var text = $"{x.Name} {x.Details}".ToLowerInvariant();
            return DevKeywords.Any(text.Contains);
        });

    private void PresetAllOn_Click(object sender, RoutedEventArgs e) => RunPreset(x => x.IsBroken ? null : true);

    private void PresetFixBroken_Click(object sender, RoutedEventArgs e) => RunPreset(x => x.IsBroken ? false : null);

    // ======================= Предпросмотр =======================

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ShowPreview = PreviewToggle.IsChecked == true;
        AppSettings.Current.Save();
        UpdatePreviewVisibility();
    }

    private void UpdatePreviewVisibility()
    {
        bool show = AppSettings.Current.ShowPreview && ActualWidth >= 1150 && _navId != Scopes.Win11
                    && !(_navId == Scopes.FileTypes && !_scopeId.StartsWith(Scopes.TypePrefix));
        PreviewPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = show ? new GridLength(290) : new GridLength(0);
        if (show) RebuildPreview();
    }

    private void RebuildPreview()
    {
        if (PreviewPanel.Visibility != Visibility.Visible || string.IsNullOrEmpty(_scopeId)) return;

        PreviewItem Std(string key) => new() { Text = Loc.T(key), IsMuted = true };
        var items = new List<PreviewItem>();
        bool isFile = _scopeId == "files" || _scopeId.StartsWith(Scopes.TypePrefix);

        if (_scopeId == "desktop")
            items.AddRange(new[] { Std("Preview.View"), Std("Preview.Sort"), Std("Preview.Refresh"), PreviewItem.Separator, Std("Preview.Paste"), PreviewItem.Separator });
        else
            items.AddRange(new[] { Std("Preview.Open"), PreviewItem.Separator });

        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var e in _all.Where(e => e.ScopeId == _scopeId && e.IsEnabled && !e.IsExtended && !e.IsBroken)
                     .OrderBy(e => e.IsSystem).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!seen.Add(e.Name)) continue;
            items.Add(new PreviewItem { Text = e.Name, Icon = e.Icon, IsHighlighted = e.IsDirty });
        }

        items.Add(PreviewItem.Separator);
        if (isFile)
            items.AddRange(new[] { Std("Preview.Cut"), Std("Preview.Copy"), PreviewItem.Separator, Std("Preview.Delete"), Std("Preview.Rename"), PreviewItem.Separator });
        else if (_scopeId != "desktop")
            items.AddRange(new[] { Std("Preview.Copy"), PreviewItem.Separator });
        items.Add(Std("Preview.Properties"));

        PreviewList.ItemsSource = items;
    }

    // ======================= Бэкапы =======================

    private void LoadBackups()
    {
        var list = BackupStore.List();
        BackupsList.ItemsSource = list;
        NoBackups.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Backup b) return;
        var r = System.Windows.MessageBox.Show(this, Loc.F("D.RestoreQ", b.CreatedText, b.Summary), Loc.T("D.RestoreTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (r != MessageBoxResult.OK) return;
        try
        {
            BackupStore.Restore(b);
            await ReloadAsync();
            ShowToast(Loc.T("T.Restored"), Loc.T("T.RestoredMsg"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Backup b) return;
        var r = System.Windows.MessageBox.Show(this, Loc.F("D.DeleteBackupQ", b.CreatedText), Loc.T("D.DeleteBackupTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        BackupStore.Delete(b);
        LoadBackups();
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupStore.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupStore.Folder}\"") { UseShellExecute = true });
    }

    // ======================= Профиль =======================

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.T("Profile.Filter"),
            FileName = $"DreddMenu_{Environment.MachineName}_{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ProfileIO.Export(dlg.FileName, _all.Where(x => !x.ScopeId.StartsWith(Scopes.TypePrefix)));
            ShowToast(Loc.T("T.Done"), Loc.F("T.Exported", Path.GetFileName(dlg.FileName)), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Profile.Filter") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var profile = ProfileIO.Load(dlg.FileName);
            if (ProfileIO.CreateMissingCustom(profile)) await ReloadAsync();
            int n = ProfileIO.ApplyStates(profile, _all);
            ApplyFilter();
            ShowToast(Loc.T("T.Done"), Loc.F("T.Imported", n), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast(Loc.T("T.Error"), Loc.T("T.ImportFail") + " " + ex.Message, InfoBarSeverity.Error);
        }
    }

    // ======================= Проверка системы =======================

    private void RunCheck_Click(object sender, RoutedEventArgs e) => ShowCheck(SelfCheck.Run(_all));

    private void ShowCheck(List<CheckResult> results)
    {
        CheckList.ItemsSource = results;
        int errors = results.Count(r => r.IsError), warnings = results.Count(r => r.IsWarning);
        CheckSummary.Text = errors > 0 ? Loc.F("Check.Summary.Errors", errors, warnings)
            : warnings > 0 ? Loc.F("Check.Summary.Warnings", warnings)
            : Loc.T("Check.Summary.Ok");
        CheckOverlay.Visibility = Visibility.Visible;
    }

    private void CloseCheck_Click(object sender, RoutedEventArgs e) => CheckOverlay.Visibility = Visibility.Collapsed;

    // ======================= О программе =======================

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void Donate_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.DonateUrl);
    private void GitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.GitHubUrl);

    // ======================= Отладочный скриншот =======================

    /// <summary>
    /// Служебный режим для проверки интерфейса: --shot файл.png [--nav раздел] [--type .ext] [--check].
    /// Делает снимок окна и закрывает программу. Настройки при этом не сохраняются.
    /// </summary>
    private async Task<bool> TryDebugScreenshotAsync()
    {
        var args = Environment.GetCommandLineArgs();
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var shot = Arg("--shot");
        if (shot == null) return false;

        if (Arg("--nav") is { } nav) NavList.SelectedItem = _nav.FirstOrDefault(n => n.Id == nav);
        if (Arg("--type") is { } ext) ShowFileType(ext);
        if (args.Contains("--check")) ShowCheck(SelfCheck.Run(_all));
        await Task.Delay(1500);

        var root = (FrameworkElement)Content;
        int w = (int)root.ActualWidth, h = (int)root.ActualHeight;
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var isLight = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Light;
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(isLight
                ? System.Windows.Media.Color.FromRgb(243, 243, 243) : System.Windows.Media.Color.FromRgb(32, 32, 32)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, w, h));
        }
        bmp.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(shot)) enc.Save(fs);

        Closing -= OnClosing;
        Application.Current.Shutdown();
        return true;
    }

    // ======================= Уведомления =======================

    private void ShowToast(string title, string message, InfoBarSeverity severity)
    {
        Toast.Title = title;
        Toast.Message = message;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
