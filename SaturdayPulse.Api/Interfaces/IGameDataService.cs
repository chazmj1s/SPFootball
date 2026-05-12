using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    public interface IGameDataService
    {
        public  Task<List<Game>> ExtractGameDataHistoryAsync(int? years);
        public  Task<int> UpdateGameDataForYearAndWeekAsync(int year, int week, CancellationToken token = default);
        public Task<int> LoadGameHistoryFromFiles();
        public Task<int> LoadTeamDataFromFile();
        public Task UpdateTeamRecordsAsync(int? targetYear = null, CancellationToken token = default);
        public Task<int> ProcessSingleFileAsync(string filePath, CancellationToken token = default);
        public Task<int> UpdateGameDataFromFileAsync(string filePath, int year, int week, CancellationToken token = default);
    }
}