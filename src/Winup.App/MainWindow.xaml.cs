using System.Collections.ObjectModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Winup.Core;

namespace Winup.App;

public sealed partial class MainWindow : Window
{
    private readonly PathEnvironment _env = PathEnvironment.Current();
    private readonly IAppProbe _probe = new WindowsAppProbe();
    private readonly ObservableCollection<CandidateItem> _items = [];
    private IReadOnlyList<Candidate> _found = [];
    private bool _busy;
    private bool _ready;

    // --demo: sample data, nothing read or changed (also used for the screenshots in the docs).
    // --scene dialog|done opens that state once the list is shown; --theme light|dark.
    private static readonly string[] Args = Environment.GetCommandLineArgs();
    private static readonly bool IsDemo = Args.Contains("--demo");
    private static string? Arg(string name) { int i = Array.IndexOf(Args, name); return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null; }

    private static readonly (string Code, string Name)[] Languages = [("en", "English"), ("uk", "Українська")];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        if (IsDemo) Title = "winup (demo)";
        var scale = Root.XamlRoot?.RasterizationScale ?? GetDpiScale();
        AppWindow.Resize(new SizeInt32((int)(1040 * scale), (int)(780 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter op) { op.PreferredMinimumWidth = (int)(720 * scale); op.PreferredMinimumHeight = (int)(520 * scale); }

        List.ItemsSource = _items;
        foreach (var (_, name) in Languages) LangBox.Items.Add(name);
        LangBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Code == Loc.Lang));
        ApplyTexts();
        _ready = true;
        Activated += OnFirstActivated;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        await ScanAsync();
        if (Arg("--scene") is null) return;
        // let the window settle first: opening the dialog within about a second of start-up
        // crashes inside WinUI (native E_INVALIDARG, no managed exception); a few seconds later it works
        while (!Root.IsLoaded || Content.XamlRoot is null) await Task.Delay(50);
        await Task.Delay(3000);
        switch (Arg("--scene"))
        {
            case "dialog": await ConfirmAndCleanAsync(); break;
            case "done": await CleanAsync(Checked()); break;
        }
    }

    private double GetDpiScale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96.0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private Level SelectedLevel => (Level)(Math.Max(0, LevelBox.SelectedIndex) + 1);

    private void ApplyTexts()
    {
        TitleText.Text = Loc.T("Cleanup");
        SubtitleText.Text = Loc.T("The check changes nothing. Tick what to clean: everything goes to the Recycle Bin, open apps are skipped.");
        RescanText.Text = Loc.T("Check again");
        FoundCaption.Text = Loc.T("Can be freed");
        SelectedCaption.Text = Loc.T("Selected");
        LevelCaption.Text = Loc.T("Select up to level");
        CleanText.Text = Loc.T("Clean selected");
        BusyText.Text = Loc.T("Checking… (nothing is changed)");
        // buttons with an icon + text need an explicit name for screen readers (and UI Automation)
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CleanButton, CleanText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(RescanButton, RescanText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LangBox, "Language");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LevelBox, LevelCaption.Text);
        int lvl = LevelBox.SelectedIndex;
        _ready = false;
        LevelBox.Items.Clear();
        foreach (var l in new[] { Level.Safe, Level.Standard, Level.Aggressive })
            LevelBox.Items.Add($"{l.Word()} — {Loc.T(l switch { Level.Safe => "rebuilds itself", Level.Standard => "downloaded again", _ => "apps may sign you out" })}");
        LevelBox.SelectedIndex = lvl < 0 ? 0 : lvl;
        _ready = true;
        Rebuild(keepChecks: true);
    }

    private void Rebuild(bool keepChecks)
    {
        var was = _items.Where(i => i.IsChecked).Select(i => i.Candidate.Id).ToHashSet();
        _items.Clear();
        foreach (var c in _found.Where(c => c.Bytes > 0 || c.NeedsAdmin))
        {
            var item = new CandidateItem(c, UpdateTotals);
            item.IsChecked = keepChecks ? was.Contains(c.Id) : c.InLevel(SelectedLevel);
            _items.Add(item);
        }
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        long canFree = _found.Where(c => c.Selectable).Sum(c => c.Bytes);
        var chosen = _items.Where(i => i.IsChecked).ToList();
        FoundValue.Text = Format.Bytes(canFree);
        SelectedValue.Text = Loc.F("{0} · {1} items", Format.Bytes(chosen.Sum(i => i.Candidate.Bytes)), chosen.Count);
        CleanButton.IsEnabled = !_busy && chosen.Count > 0;
        RescanButton.IsEnabled = !_busy;
        if (!_busy && Status.Severity == InfoBarSeverity.Informational)
        {
            int blocked = _found.Count(c => c.OpenApps.Count > 0);
            Status.Title = "";
            Status.Message = blocked > 0
                ? Loc.F("{0} items are skipped while their apps are open. Close them and check again.", blocked)
                : Loc.T("Deleted files go to the Recycle Bin, so you can restore them.");
        }
    }

    private async Task ScanAsync()
    {
        SetBusy(true, Loc.T("Checking… (nothing is changed)"));
        try
        {
            _found = IsDemo ? Demo.Candidates() : await Task.Run(() => new Scanner(_env, _probe).Check(RuleSet.LoadDefault()));
            Rebuild(keepChecks: false);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Loc.T("The check failed"), ex.Message);
        }
        finally { SetBusy(false, ""); }
    }

    private void SetBusy(bool busy, string text)
    {
        _busy = busy;
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BusyText.Text = text;
        UpdateTotals();
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        Status.Severity = severity;
        Status.Title = title;
        Status.Message = message;
    }

    private async void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        Status.Severity = InfoBarSeverity.Informational;
        await ScanAsync();
    }

    private void LevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LevelBox.SelectedIndex < 0) return;
        foreach (var i in _items) i.IsChecked = i.Candidate.InLevel(SelectedLevel);
    }

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LangBox.SelectedIndex < 0) return;
        Loc.Lang = Languages[LangBox.SelectedIndex].Code;
        ApplyTexts();
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e) => await ConfirmAndCleanAsync();

    private async Task ConfirmAndCleanAsync()
    {
        var chosen = Checked();
        if (chosen.Count == 0) return;
        long total = chosen.Sum(c => c.Bytes);

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = Loc.F("{0} items ({1}) will be moved to the Recycle Bin. Apps that are open by then are skipped.", chosen.Count, Format.Bytes(total)),
            TextWrapping = TextWrapping.Wrap,
        });
        var list = new StackPanel { Spacing = 4 };
        foreach (var c in chosen)
            list.Children.Add(new TextBlock { Text = $"•  {c.Rule.LocalizedLabel} — {Format.Bytes(c.Bytes)}", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260 });

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Loc.T("Clean the selected items?"),
            CloseButtonText = Loc.T("Cancel"),
            Content = body,
        };
        dialog.PrimaryButtonText = Loc.T("Move to Recycle Bin");
        dialog.DefaultButton = ContentDialogButton.Close; // Enter cancels
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await CleanAsync(chosen);
    }

    private List<Candidate> Checked() => _items.Where(i => i.IsChecked && i.Candidate.Selectable).Select(i => i.Candidate).ToList();

    private async Task CleanAsync(List<Candidate> chosen)
    {
        if (chosen.Count == 0) return;
        SetBusy(true, Loc.T("Moving to the Recycle Bin…"));
        try
        {
            CleanResult result;
            if (IsDemo)
            {
                await Task.Delay(600);
                result = Demo.Clean(chosen); // nothing is touched
            }
            else
            {
                result = await Task.Run(() =>
                {
                    var r = new Cleaner(_env, new RecycleBin(), _probe).Clean(chosen);
                    new HistoryStore(_env).Save(r);
                    return r;
                });
            }
            var msg = Loc.F("Moved to the Recycle Bin: {0}. The space is freed when you empty the Recycle Bin.", Format.Bytes(result.FreedBytes));
            int skipped = result.Skipped.Count();
            if (result.Failed > 0 || skipped > 0) msg += " " + Loc.F("Skipped: {0} in use or refused.", result.Failed + skipped);
            ShowStatus(InfoBarSeverity.Success, Loc.T("Done"), msg);
            var cleaned = chosen.Select(c => c.Id).ToHashSet();
            _found = IsDemo
                ? _found.Where(c => !cleaned.Contains(c.Id)).ToList()
                : await Task.Run(() => new Scanner(_env, _probe).Check(RuleSet.LoadDefault()));
            Rebuild(keepChecks: false);
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, Loc.T("Cleanup failed"), ex.Message);
        }
        finally { SetBusy(false, ""); }
    }
}
