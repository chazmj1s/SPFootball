using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
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

        // Register Services
        builder.Services.AddSingleton<GameDataApiService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            return new GameDataApiService(httpClient);
        });

        builder.Services.AddSingleton<PredictionApiService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            return new PredictionApiService(httpClient);
        });

        // Register ViewModels
        builder.Services.AddTransient<PowerRankingsViewModel>();
        builder.Services.AddTransient<ScheduleViewModel>();
        builder.Services.AddTransient<FollowingViewModel>();
        builder.Services.AddTransient<ProjectionsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();

        // Register Pages
        builder.Services.AddTransient<PowerRankingsPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<FollowingPage>();
        builder.Services.AddTransient<ProjectionsPage>();
        builder.Services.AddTransient<ConfigPage>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
