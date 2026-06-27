using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VidSlice.Services;
using VidSlice.ViewModels;
using Wpf.Ui.Appearance;

namespace VidSlice;

/// <summary>
/// Interaction logic for App.xaml. Builds a generic host for DI + logging,
/// applies the saved theme, and shows the main window (passing any file paths
/// received on the command line, e.g. from a "Send to VidSlice" shortcut).
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VidSlice");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch anything that escapes a command's own try/catch so the app logs
        // and reports instead of vanishing.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new FileLoggerProvider(Path.Combine(AppDataDir, "logs")));
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISettingsService>(_ => new SettingsService());
                services.AddSingleton<IFfmpegService, FfmpegService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Apply the saved theme before the window appears.
        var settings = _host.Services.GetRequiredService<ISettingsService>().Load();
        ApplyTheme(settings.Theme);

        var window = _host.Services.GetRequiredService<MainWindow>();

        // Files passed on the command line (Send To / file association).
        var files = e.Args.Where(File.Exists).ToArray();
        if (files.Length > 0)
            window.ViewModel.AddFiles(files);

        window.Show();
    }

    /// <summary>Apply a theme name ("Dark"/"Light"/"System") via WPF-UI.</summary>
    public static void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "Light": ApplicationThemeManager.Apply(ApplicationTheme.Light); break;
            case "Dark": ApplicationThemeManager.Apply(ApplicationTheme.Dark); break;
            default: ApplicationThemeManager.ApplySystemTheme(); break;
        }
    }

    private ILogger? Logger => _host?.Services.GetService<ILoggerFactory>()?.CreateLogger("App");

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger?.LogError(e.Exception, "Unhandled UI exception");
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe app will keep running. " +
            $"Details were written to the log in {AppDataDir}.",
            "VidSlice", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the app alive
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Logger?.LogError(e.ExceptionObject as Exception, "Unhandled domain exception (terminating={Terminating})", e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        Logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Services.GetService<MainViewModel>()?.SaveSettings();
        }
        catch { /* best effort */ }

        _host?.Dispose();
        base.OnExit(e);
    }
}
