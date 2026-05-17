using SaturdayPulse.Contracts;
using SaturdayPulse.Extensions;

namespace SaturdayPulse.Utilities
{
    public class RecordProcessor(IUnitOfWork _uow)
    {
        public async Task ProcessSingleRecordAsync(string[] cells, string yearIn, CancellationToken token)
        {
            if (cells.Length >= 9)
            {
                // Load teams into memory to perform string matching in C# instead of SQL
                var teams = await _uow.Team.GetAllAsync(token);

                // Use extension method to convert string array to Game
                var gameData = cells.ToGame(yearIn, teams);

                await _uow.Game.AddRangeAsync([gameData], token);
                await _uow.SaveChangesAsync(token);
            }
        }
    }
}
