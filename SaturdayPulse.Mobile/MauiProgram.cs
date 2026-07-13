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
            "Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXpfcXVQR2lfWUB+V0RWYEo=");
        
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
