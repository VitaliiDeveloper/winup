using Microsoft.UI.Xaml;
using Winup.Core;

namespace Winup.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // --theme light|dark: the whole app (theme brushes, caption buttons), not just one element
        var argv = Environment.GetCommandLineArgs();
        int t = Array.IndexOf(argv, "--theme");
        if (t >= 0 && t + 1 < argv.Length && argv[t + 1] is "light" or "dark")
            RequestedTheme = argv[t + 1] == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                var dir = PathEnvironment.Current().DataDir;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}\n{e.Exception}\n\n");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(argv, "--lang");
        if (i >= 0 && i + 1 < argv.Length) Loc.Lang = argv[i + 1];
        _window = new MainWindow();
        _window.Activate();
    }
}
