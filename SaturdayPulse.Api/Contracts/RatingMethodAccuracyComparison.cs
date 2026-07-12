namespace SaturdayPulse.Contracts
{
    /// <summary>
    /// One method's grading against actual results for a set of games. Shape
    /// deliberately mirrors ProjectionAccuracyService's existing output (mae,
    /// winnerAccuracyPct, spreadBias, totalMae/totalBias) so production and
    /// experimental numbers can be compared directly, field by field, without
    /// mentally translating between two different naming schemes.
    /// </summary>
    public record AccuracyStats(
        int Games,
        decimal Mae,
        decimal WinnerAccuracyPct,
        decimal SpreadBias,
        decimal TotalMae,
        decimal TotalBias);

    /// <summary>One week's production vs. experimental vs. Vegas accuracy, side by side.
    /// Vegas fields are nullable — not every game has a recorded line.</summary>
    public record WeeklyAccuracyComparison(
        int Week,
        AccuracyStats Production,
        AccuracyStats Experimental,
        AccuracyStats? VegasClosing,
        AccuracyStats? VegasOpening);

    /// <summary>One location split's (Home vs Neutral) production/experimental/Vegas
    /// accuracy. Neutral-site games get zero HFA applied by CalculatePrediction —
    /// comparing spreadBias between the two splits isolates whether a bias is
    /// specifically an HFA-tuning issue (concentrated in Home) vs. something more
    /// general (present in both).</summary>
    public record LocationAccuracyComparison(
        string Location,
        AccuracyStats Production,
        AccuracyStats Experimental,
        AccuracyStats? VegasClosing,
        AccuracyStats? VegasOpening);

    /// <summary>
    /// Top-level result of grading both rating methods — and Vegas, for context —
    /// against real outcomes for a year/week range. VegasOpening is the fairer
    /// comparison against our own predictions (closing lines have absorbed
    /// information, e.g. injuries/weather/sharp money, that neither our model nor
    /// a same-day prediction would have); VegasClosing included too since it's
    /// still useful context for "how good is this line by kickoff."
    /// </summary>
    public record RatingMethodAccuracyComparison(
        int Year,
        int TotalGames,
        AccuracyStats Production,
        AccuracyStats Experimental,
        AccuracyStats? VegasClosing,
        AccuracyStats? VegasOpening,
        List<WeeklyAccuracyComparison> ByWeek,
        List<LocationAccuracyComparison> ByLocation);
}
