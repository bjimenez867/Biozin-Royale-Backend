namespace Biozin_Royale_Backend.Dominio.Entities;

/// Mapea la tabla public.bets. Cada fila es una apuesta individual, no un agregado
/// (a diferencia de UserStatistics, que agrega estas mismas filas por usuario).
public class GamesHistory
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string GameType { get; set; } = string.Empty;
    public Guid? RoundId { get; set; }
    public decimal Amount { get; set; }
    public decimal Payout { get; set; }
    public decimal Profit { get; set; }
    public string Result { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
