using Microsoft.Maui.Devices;

namespace SaturdayPulse.Mobile.Services;

public static class ApiConfiguration
{
#if ANDROID
    const string LocalApiUrl = "https://10.0.2.2:7010/api/productiongamedata/";
#else
    const string LocalApiUrl = "https://localhost:7010/api/productiongamedata/";
#endif
    const string ProductionApiUrl = "https://ncaa-power-ratings-api-ftdyg2bxhpfxc9an.westus2-01.azurewebsites.net/api/productionGameData/";
    public static string BaseUrl =>
#if DEBUG
         LocalApiUrl;
#else
        ProductionApiUrl;
#endif

    // Sibling of BaseUrl, one level up — for controllers that don't sit
    // under [Route("api/productiongamedata")], e.g. UserController at
    // api/user/... (UserApiService uses this, not BaseUrl).
#if ANDROID
    const string LocalApiRootUrl = "https://10.0.2.2:7010/api/";
#else
    const string LocalApiRootUrl = "https://localhost:7010/api/";
#endif
    const string ProductionApiRootUrl = "https://ncaa-power-ratings-api-ftdyg2bxhpfxc9an.westus2-01.azurewebsites.net/api/";
    public static string ApiRootUrl =>
#if DEBUG
         LocalApiRootUrl;
#else
        ProductionApiRootUrl;
#endif

    public const string Audience = "https://api.j1stx.com";
    public const string DiscordWebhook = "https://discordapp.com/api/webhooks/1529270867761168395/l8QXZRf3nk8vPdKucQWddTHobsf8ebDC4S7PCij4V9ugg5GXJztq_jYkj7i33fLDEv0j";
}
