using Microsoft.UI.Xaml;

namespace MicaSound.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var win = new MainWindow();
        _window = win;
        _ = win.InitializeAsync();
        _window.Activate();
    }
}