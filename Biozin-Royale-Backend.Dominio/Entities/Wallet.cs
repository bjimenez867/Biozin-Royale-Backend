namespace Biozin_Royale_Backend.Dominio.Entities;

public class Wallet
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
