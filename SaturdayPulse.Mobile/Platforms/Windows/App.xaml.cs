using Microsoft.UI.Xaml;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Auth0.OidcClient.Platforms.Windows;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SaturdayPulse.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		// Auth0's documented workaround for the WebAuthenticator/AppxManifest
		// issue on Windows (dotnet/maui GH #2702) — catches the redirect back
		// from the system browser after Auth0 login and hands it to the SDK.
		// MUST run before InitializeComponent(): if this returns true, the app
		// is being re-activated purely to deliver a login redirect, not to
		// launch normally, so InitializeComponent() (which would spin up a
		// second full app instance) is skipped for that activation.
		if (Auth0.OidcClient.Platforms.Windows.Activator.Default.CheckRedirectionActivation())
			return;

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
