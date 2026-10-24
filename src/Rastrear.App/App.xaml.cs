using Microsoft.UI.Xaml;

namespace Rastrear.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        if (Program.VideoArg is { } video)
        {
            _ = _window.OpenVideoFileAsync(video);
        }
    }
}
