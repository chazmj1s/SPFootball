using Microsoft.Maui;
using Microsoft.Maui.Controls;
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