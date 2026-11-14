using System.IO;
using System.Windows;
using System.Windows.Controls;
using DreddMenu.Core;
using Wpf.Ui.Controls;

namespace DreddMenu;

public partial class AddItemWindow : FluentWindow
{
    private sealed record TemplateOption(string Title, CustomItems.Template? Template);

    private readonly Dictionary<string, CheckBox> _targets = new();

    public string CreatedName { get; private set; } = "";

    public AddItemWindow(string preselectTarget)
    {
        InitializeComponent();

        foreach (var t in CustomItems.Targets)
        {
            var cb = new CheckBox { Content = Loc.T(t.LabelKey), Margin = new Thickness(0, 0, 16, 6), IsChecked = t.Id == preselectTarget };
            _targets[t.Id] = cb;
            TargetsPanel.Children.Add(cb);
        }

        var options = new List<TemplateOption> { new(Loc.T("Add.Template.None"), null) };
        options.AddRange(CustomItems.AvailableTemplates().Select(t => new TemplateOption(Loc.T(t.NameKey), t)));
        TemplateCombo.ItemsSource = options;
        TemplateCombo.SelectedIndex = 0;
    }

    private void TemplateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is not TemplateOption { Template: { } t } opt) return;
        NameBox.Text = opt.Title;
        ExeBox.Text = t.Exe;
        ArgsBox.Text = t.Args;
        foreach (var (id, cb) in _targets) cb.IsChecked = t.Targets.Contains(id);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Add.Browse.Filter") };
        if (dlg.ShowDialog(this) != true) return;
        ExeBox.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Loc.F("Add.OpenIn", Path.GetFileNameWithoutExtension(dlg.FileName));
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var exe = ExeBox.Text.Trim().Trim('"');
        var targets = _targets.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToList();

        string? error = string.IsNullOrEmpty(name) ? Loc.T("Add.Err.Name")
            : string.IsNullOrEmpty(exe) || !File.Exists(Environment.ExpandEnvironmentVariables(exe)) ? Loc.T("Add.Err.Program")
            : targets.Count == 0 ? Loc.T("Add.Err.Where")
            : null;
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            CustomItems.Create(name, exe, ArgsBox.Text.Trim(), targets, ShiftBox.IsChecked == true);
            CreatedName = name;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
