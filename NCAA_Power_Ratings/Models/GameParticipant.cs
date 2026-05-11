public class GameParticipant
{
    public int TeamId { get; set; }
    public string TeamDivision { get; set; } = "";
    public int OpponentId { get; set; }
    public string OpponentDivision { get; set; } = "";
    public int TeamPoints { get; set; }
    public int OpponentPoints { get; set; }
    public char Location { get; set; }
    public bool IsHomeTeam { get; set; }
}