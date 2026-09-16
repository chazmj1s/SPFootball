using Auth0.OidcClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using SaturdayPulse.Helpers;
using SaturdayPulse.Mobile.Services;
using SaturdayPulse.Services;
using SaturdayPulse.ViewModels;
using SaturdayPulse.Views;
using Syncfusion.Licensing;
using Syncfusion.Maui.Core.Hosting;

namespace SaturdayPulse;

public static class MauiProgram
{
    // Android emulator hits the local dev API over the loopback alias
    // (10.0.2.2) using the ASP.NET Core self-signed dev cert, which Android
    // does not trust by default. Rather than fighting Android's CA-install
    // UI, this bypasses server certificate validation ONLY for DEBUG builds
    // on Android — Release builds and every other platform still validate
    // real certificates normally. Production Android traffic (ApiConfiguration.
    // ProductionApiUrl) is unaffected since this only ever compiles into
    // DEBUG builds.
#if DEBUG && ANDROID
    static HttpMessageHandler GetInsecureAndroidHandler() =>
        new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
#endif

    public static MauiApp CreateMauiApp()
    {
        SyncfusionLicenseProvider.RegisterLicense(
            "Ngo9BigBOggjHTQxAR8/V1JAaF5cXmJCd1p/TH5YfUNzdUVEY1ZUTXxaS1ZhSXxVdkJhWH5fdX1RRmFeUUB9XEY=");
        
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureSyncfusionCore()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register HttpClient
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<FollowService>();
        builder.Services.AddSingleton<PersonalGameService>();
        builder.Services.AddSingleton<SharedNavigationStateService>();
        builder.Services.AddSingleton<GameDataCacheService>();
        builder.Services.AddSingleton<RankingsCacheService>();
        builder.Services.AddSingleton<TeamCacheService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<FeedbackService>();
        // EntitlementService depends on AuthService + UserApiService (both
        // registered elsewhere in this method) — DI resolves the graph at
        // request time, so registration order here doesn't matter.
        builder.Services.AddSingleton<EntitlementService>();
        builder.Services.AddSingleton<ContentApiService>();

        // Register Services
        var gameDataClientBuilder = builder.Services.AddHttpClient<GameDataApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
#if DEBUG && ANDROID
        gameDataClientBuilder.ConfigurePrimaryHttpMessageHandler(GetInsecureAndroidHandler);
#endif

        var predictionClientBuilder = builder.Services.AddHttpClient<PredictionApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
#if DEBUG && ANDROID
        predictionClientBuilder.ConfigurePrimaryHttpMessageHandler(GetInsecureAndroidHandler);
#endif

        // FollowService/PersonalGameService pull this in automatically via
        // constructor injection — no changes needed to their registrations
        // above. X-User-Id is attached per-request inside UserApiService
        // itself, so no extra DefaultRequestHeaders wiring needed here.
        var userApiClientBuilder = builder.Services.AddHttpClient<UserApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.ApiRootUrl);
            client.Timeout = TimeSpan.FromSeconds(15);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        builder.Services.AddHttpClient<ContentApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.ApiRootUrl);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

#if DEBUG && ANDROID
        userApiClientBuilder.ConfigurePrimaryHttpMessageHandler(GetInsecureAndroidHandler);
#endif

        // Auth0 login (Windows + iOS wired first — see MainPage/Settings for
        // where LoginAsync/LogoutAsync actually get called; that's not built
        // yet, this is just the client registration). RedirectUri/
        // PostLogoutRedirectUri use a placeholder scheme (j1ssports://)
        // independent of the app's still-unsettled public name — update this
        // and the matching Info.plist/Package.appxmanifest entries together
        // if that changes. Matches the Allowed Callback/Logout URLs
        // configured on the Native Application in the Auth0 dashboard.
        builder.Services.AddSingleton(new Auth0Client(new Auth0ClientOptions
        {
            Domain = "dev-uj415yajuff2lsqw.us.auth0.com",
            ClientId = "uWtUlEKnLY6ZiXlS38BkzwTTGIa20mA5",
            RedirectUri = "j1ssports://callback",
            PostLogoutRedirectUri = "j1ssports://callback",
            Scope = "openid profile email"
        }));

        // Register ViewModels
        builder.Services.AddSingleton<PowerRankingsViewModel>();
        builder.Services.AddSingleton<ScheduleViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<PostseasonViewModel>();
        builder.Services.AddSingleton<SandboxViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MyTeamsViewModel>();

        // Register Pages
        builder.Services.AddSingleton<MyTeamsPage>(); 
        builder.Services.AddSingleton<PowerRankingsPage>();
        builder.Services.AddSingleton<SchedulePage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<PostseasonPage>();
        builder.Services.AddSingleton<SandboxPage>();
        builder.Services.AddSingleton<MainPage>();

        // Android: suppress WebView's automatic algorithmic dark-mode inversion,
        // which overrides our own theme-matched CSS/background based on the
        // OS-level dark mode setting rather than the app's UserAppTheme.
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("DisableForceDark", (handler, view) =>
        {
#if ANDROID
    var settings = handler.PlatformView.Settings;
    if (OperatingSystem.IsAndroidVersionAtLeast(33))
    {
        settings.AlgorithmicDarkeningAllowed = false;
    }
#endif
        });

        // iOS/Mac Catalyst: WKWebView doesn't reliably re-run LoadHtmlString when
        // Source is swapped to a new HtmlWebViewSource on a recycled CollectionView
        // cell (theme-change re-render of Content sections). Force it explicitly.
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping(nameof(IWebView.Source), (handler, view) =>
        {
#if IOS || MACCATALYST
    if (view.Source is HtmlWebViewSource htmlSource)
    {
        handler.PlatformView.LoadHtmlString(htmlSource.Html, baseUrl: null);
    }
#elif WINDOWS
            // WebView2 defaults PreferredColorScheme to Auto (follows Windows OS
            // dark mode), independent of Application.Current.UserAppTheme. Force it
            // to match our own theme logic instead of the OS setting.
            var isDark = Application.Current?.RequestedTheme
                == Microsoft.Maui.ApplicationModel.AppTheme.Dark;
            var scheme = isDark
                ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;

            void ApplyScheme()
            {
                if (handler.PlatformView.CoreWebView2 != null)
                    handler.PlatformView.CoreWebView2.Profile.PreferredColorScheme = scheme;
            }

            if (handler.PlatformView.CoreWebView2 != null)
                ApplyScheme();
            else
                handler.PlatformView.CoreWebView2Initialized += (_, _) => ApplyScheme();
#endif
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
