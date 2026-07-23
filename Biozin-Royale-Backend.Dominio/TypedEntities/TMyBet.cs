namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TMyBet
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public decimal TotalOdds { get; set; }
    public decimal PotentialWin { get; set; }
    public string Status { get; set; } = string.Empty; // pending/won/lost
    public decimal Payout { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public List<TBetSelectionDetail> Selections { get; set; } = [];
}
