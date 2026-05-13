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
}