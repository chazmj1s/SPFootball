namespace SaturdayPulse.Utilities
{
    public static class ColumnMap
    {
        public static ColumnIndexes ForYear(int year) => year switch
        {
            // 2013+ format: Rk, Wk, Date, Time, Day, Winner, Pts, Location, Loser, Pts, Notes
            >= 2013 => new ColumnIndexes(
                RowId: 0,
                Week: 1,
                Date: 2,
                // Time: 3 — intentionally skipped
                Day: 4,
                WinnerName: 5,
                WPoints: 6,
                Location: 7,
                LoserName: 8,
                LPoints: 9
            ),

            // Old format: Rk, Wk, Date, Day, Winner, Pts, Location, Loser, Pts, Notes
            _ => new ColumnIndexes(
                RowId: 0,
                Week: 1,
                Date: 2,
                Day: 3,
                WinnerName: 4,
                WPoints: 5,
                Location: 6,
                LoserName: 7,
                LPoints: 8
            )
        };

        public record ColumnIndexes(
            int RowId,
            int Week,
            int Date,
            int Day,
            int WinnerName,
            int WPoints,
            int Location,
            int LoserName,
            int LPoints
        );
    }
}