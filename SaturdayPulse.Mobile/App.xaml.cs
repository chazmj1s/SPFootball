using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace SaturdayPulse;

public partial class App : Application
{
    public App()
    {
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