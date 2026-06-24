namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TGamesHistory
{
    public Guid Id { get; set; }
    public string GameType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Payout { get; set; }
    public decimal Profit { get; set; }
    public string Result { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
