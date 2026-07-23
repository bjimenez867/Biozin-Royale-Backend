namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TBetSelectionDetail
{
    public string ExternalMatchId { get; set; } = string.Empty;
    public string SportKey { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public string Team1 { get; set; } = string.Empty;
    public string Team2 { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty; // home/draw/away
    public decimal Odds { get; set; }
    public DateTime CommenceTimeUtc { get; set; }
    public string Status { get; set; } = "pending"; // pending/won/lost
}
