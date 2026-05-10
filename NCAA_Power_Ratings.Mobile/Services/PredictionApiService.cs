using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Devices;

namespace NCAA_Power_Ratings.Mobile.Services
{
    /// <summary>
    /// Service for calling the Production Game Data API.
    /// </summary>
    public class PredictionApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public PredictionApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            // TODO: Update this to your actual API URL when deployed
            // For local testing on Android emulator, use 10.0.2.2
            // For local testing on iOS simulator, use localhost
#if DEBUG
            _baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5086/api/productiongamedata"
                : "http://localhost:5086/api/productiongamedata";
#else
            _baseUrl = "https://ncaa-power-ratings-api-ftdyg2bxhpfxc9an.westus2-01.azurewebsites.net/api/productionGameData";
#endif
        }

        /// <summary>
        /// Predicts a single matchup.
        /// </summary>
        public async Task<GamePredictionResponse?> PredictMatchupAsync(
            int year,
            string teamName,
            string opponentName,
            char location = 'N',
            int week = 0)
        {
            try
            {
                var url = $"{_baseUrl}/predictMatchup?year={year}&teamName={Uri.EscapeDataString(teamName)}&opponentName={Uri.EscapeDataString(opponentName)}&location={location}&week={week}";
                var response = await _httpClient.GetFromJsonAsync<GamePredictionResponse>(url);
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error predicting matchup: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets rivalries filtered by tier.
        /// </summary>
        public async Task<RivalriesResponse?> GetRivalriesAsync(string? tier = null, int? minGames = null)
        {
            try
            {
                var url = $"{_baseUrl}/rivalries";
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(tier))
                    queryParams.Add($"tier={tier}");
                if (minGames.HasValue)
                    queryParams.Add($"minGames={minGames}");

                if (queryParams.Any())
                    url += "?" + string.Join("&", queryParams);

                var response = await _httpClient.GetFromJsonAsync<RivalriesResponse>(url);
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting rivalries: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Queries team records with filters.
        /// </summary>
        public async Task<TeamRecordsResponse?> QueryTeamRecordsAsync(
            int? startYear = null,
            int? endYear = null,
            int? minWins = null,
            int limit = 50)
        {
            try
            {
                var url = $"{_baseUrl}/queryTeamRecords?limit={limit}";

                if (startYear.HasValue)
                    url += $"&startYear={startYear}";
                if (endYear.HasValue)
                    url += $"&endYear={endYear}";
                if (minWins.HasValue)
                    url += $"&minWins={minWins}";

                var response = await _httpClient.GetFromJsonAsync<TeamRecordsResponse>(url);
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying team records: {ex.Message}");
                return null;
            }
        }
    }

    #region Response Models

    public class GamePredictionResponse
    {
        public string Matchup { get; set; } = string.Empty;
        public string Prediction { get; set; } = string.Empty;
        public double ExpectedMargin { get; set; }
        public double MarginOfError { get; set; }
        public string Confidence { get; set; } = string.Empty;
        public string TeamRecord { get; set; } = string.Empty;
        public string OpponentRecord { get; set; } = string.Empty;
        public double? TeamPowerRating { get; set; }
        public double? OpponentPowerRating { get; set; }
        public string? RivalryNote { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    public class RivalriesResponse
    {
        public int TotalMatchups { get; set; }
        public int TotalInDatabase { get; set; }
        public List<Rivalry> Rivalries { get; set; } = new();
    }

    public class Rivalry
    {
        public string Team1 { get; set; } = string.Empty;
        public string Team2 { get; set; } = string.Empty;
        public string RivalryName { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public int GamesPlayed { get; set; }
        public double AvgMargin { get; set; }
        public double StDevMargin { get; set; }
        public double UpsetRate { get; set; }
        public double VarianceRatio { get; set; }
        public int SeriesAge { get; set; }
        public int FirstPlayed { get; set; }
        public int LastPlayed { get; set; }
    }

    public class TeamRecordsResponse
    {
        public int Count { get; set; }
        public List<TeamRecord> Results { get; set; } = new();
    }

    public class TeamRecord
    {
        public int Year { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string Record { get; set; } = string.Empty;
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }
        public int PointDifferential { get; set; }
        public double? BaseSOS { get; set; }
        public double? SubSOS { get; set; }
        public double? CombinedSOS { get; set; }
        public double? PowerRating { get; set; }
    }

    #endregion
}
