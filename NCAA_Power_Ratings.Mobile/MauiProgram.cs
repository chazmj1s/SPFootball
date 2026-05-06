using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Controls.Hosting;
using NCAA_Power_Ratings.Mobile.Services;
using NCAA_Power_Ratings.Mobile.ViewModels;
using NCAA_Power_Ratings.Mobile.Views;

namespace NCAA_Power_Ratings.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
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
