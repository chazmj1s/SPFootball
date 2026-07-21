using Microsoft.Maui.Devices;

namespace SaturdayPulse.Mobile.Services;

public static class ApiConfiguration
{
    const string LocalApiUrl = "https://localhost:7010/api/productiongamedata/";
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
    const string LocalApiRootUrl = "https://localhost:7010/api/";
    const string ProductionApiRootUrl = "https://ncaa-power-ratings-api-ftdyg2bxhpfxc9an.westus2-01.azurewebsites.net/api/";
    public static string ApiRootUrl =>
#if DEBUG
         LocalApiRootUrl;
#else
        ProductionApiRootUrl;
#endif

    public const string Audience = "https://api.j1stx.com";
}