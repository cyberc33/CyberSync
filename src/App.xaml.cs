using Microsoft.UI.Xaml;

namespace CyberSync;

public partial class App : Application
{
    private AppRuntime? _runtime;
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--run-scheduled", StringComparison.OrdinalIgnoreCase))) {
            _runtime ??= new AppRuntime();
            int exitCode = await _runtime.RunScheduledAsync();
            Environment.Exit(exitCode);
            return;
        }

        _runtime ??= new AppRuntime();
        var mainWindow = new MainWindow(_runtime);
        _window = mainWindow;
        mainWindow.Activate();
        mainWindow.InitializeWindowIntegration();
    }
}
