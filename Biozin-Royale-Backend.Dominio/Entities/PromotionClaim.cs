namespace Biozin_Royale_Backend.Dominio.Entities;

public class PromotionClaim
{
    public Guid Id { get; set; }
    public Guid PromotionId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
