using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Auth0.OidcClient;


namespace SaturdayPulse;

public partial class App : Application
{
    public App()
    {
#if WINDOWS
        if (Auth0.OidcClient.Platforms.Windows.Activator.Default.CheckRedirectionActivation())
            return;
#endif

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
}