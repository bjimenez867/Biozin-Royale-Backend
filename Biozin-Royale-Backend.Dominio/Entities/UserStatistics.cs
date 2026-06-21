namespace Biozin_Royale_Backend.Dominio.Entities;

/// Mapea la vista public.user_statistics (agrega bets.status = 'settled' por user_id).
/// Sin PK propia: es de solo lectura, una fila por usuario.
public class UserStatistics
{
    public Guid UserId { get; set; }
    public long PartidasJugadas { get; set; }
    public long PartidasGanadas { get; set; }
    public decimal ApostadoTotal { get; set; }
    public decimal GananciasNetas { get; set; }
}
