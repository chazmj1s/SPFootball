using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Auth0.OidcClient;
using SaturdayPulse.Helpers;


namespace SaturdayPulse;

public partial class App : Application
{
    // Crash capture: the in-app Status log lives in memory, so a fatal
    // exception would wipe it before anyone could read it. Unhandled
    // exceptions are written to this small file instead and replayed into
    // AppLogger (Settings > Status, admin only) on the next launch.
    private const string CrashFileName = "last_crash.txt";
    private const int MaxCrashFileChars = 8000;

    public App()
    {
#if WINDOWS
        if (Auth0.OidcClient.Platforms.Windows.Activator.Default.CheckRedirectionActivation())
            return;
#endif

        RegisterCrashCapture();

        InitializeComponent();

        // Restore the theme SettingsViewModel.AppTheme persisted via
        // Preferences — that setter only applies UserAppTheme live during
        // the session it's changed in; nothing re-applies it on a cold
        // launch until now. Key/default ("System") must stay identical to
        // SettingsViewModel.AppThemeKey.
        UserAppTheme = Preferences.Default.Get("AppTheme", "System") switch
        {
            "Light" => AppTheme.Light,
            "Dark"  => AppTheme.Dark,
            _       => AppTheme.Unspecified
        };

        ReplayPreviousCrash();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            window.Width = 414;
            window.Height = 896;
        }

        return window;
    }

    private static string CrashFilePath => Path.Combine(FileSystem.AppDataDirectory, CrashFileName);

    /// <summary>Hooks managed unhandled-exception events. Handlers only write
    /// the file (no AppLogger/UI work): they can run on any thread while the
    /// process is going down.</summary>
    private static void RegisterCrashCapture()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var source = e.IsTerminating ? "UnhandledException (fatal)" : "UnhandledException";
            RecordCrash(source, e.ExceptionObject as Exception, e.ExceptionObject?.ToString());
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            RecordCrash("UnobservedTaskException", e.Exception, null);
            e.SetObserved();
        };
    }

    private static void RecordCrash(string source, Exception? ex, string? fallback)
    {
        try
        {
            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex?.ToString() ?? fallback ?? "unknown exception"}";

            // Also visible in Console.app / Xcode device console when the phone is
            // connected to the Mac (no debugger needed, so it works for TestFlight builds).
            Console.Error.WriteLine($"[MAUI_CRASH] {entry}");
#if IOS
            AppleLogger.LogToMacConsole(entry);
#endif

            var path = CrashFilePath;
            var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            var combined = existing.Length == 0 ? entry : existing + Environment.NewLine + entry;

            if (combined.Length > MaxCrashFileChars)
                combined = combined[^MaxCrashFileChars..];

            File.WriteAllText(path, combined);
        }
        catch
        {
            // Nothing safe to do while crashing; never throw from a crash handler.
        }
    }

    /// <summary>Called on the main thread at launch: moves any recorded crash
    /// into the in-app log and deletes the file so it shows once.</summary>
    private static void ReplayPreviousCrash()
    {
        try
        {
            var path = CrashFilePath;
            if (!File.Exists(path)) return;

            var text = File.ReadAllText(path);
            File.Delete(path);

            if (!string.IsNullOrWhiteSpace(text))
                AppLogger.Log($"[Crash] Recorded from a previous session:{Environment.NewLine}{text}", SaturdayPulse.Helpers.LogLevel.Error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Crash] Replay failed: {ex.Message}");
        }
    }
}