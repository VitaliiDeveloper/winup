using Microsoft.UI.Xaml;
using Winup.Core;

namespace Winup.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
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
