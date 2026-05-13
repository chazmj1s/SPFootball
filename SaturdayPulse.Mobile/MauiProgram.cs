using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
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
        builder.Services.AddTransient<PowerRankingsViewModel>();
        builder.Services.AddTransient<ScheduleViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ProjectionsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();

        // Register Pages
        builder.Services.AddTransient<PowerRankingsPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ProjectionsPage>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
