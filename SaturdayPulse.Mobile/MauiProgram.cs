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

        // Register Services
        builder.Services.AddHttpClient<GameDataApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        builder.Services.AddHttpClient<PredictionApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(15);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // FollowService/PersonalGameService pull this in automatically via
        // constructor injection — no changes needed to their registrations
        // above. X-User-Id is attached per-request inside UserApiService
        // itself, so no extra DefaultRequestHeaders wiring needed here.
        builder.Services.AddHttpClient<UserApiService>(client =>
        {
            client.BaseAddress = new Uri(ApiConfiguration.ApiRootUrl);
            client.Timeout = TimeSpan.FromSeconds(15);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
