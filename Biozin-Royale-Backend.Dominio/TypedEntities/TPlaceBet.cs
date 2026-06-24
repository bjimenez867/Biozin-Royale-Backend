namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TPlaceBetRequest
{
    public decimal Amount { get; set; }
    public decimal TotalOdds { get; set; }
    public List<TBetSelection> Selections { get; set; } = [];
}

public class TBetSelection
{
    public int MatchId { get; set; }
    public string Team1 { get; set; } = string.Empty;
    public string Team2 { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public decimal Odds { get; set; }
}

public class TBetResult
{
    public Guid BetId { get; set; }
    public decimal NewBalance { get; set; }
    public decimal PotentialWin { get; set; }
}
