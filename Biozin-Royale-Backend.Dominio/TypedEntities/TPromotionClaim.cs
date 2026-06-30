namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TPromotionClaim
{
    public Guid Id { get; set; }
    public Guid PromotionId { get; set; }
    public TPromotion? Promotion { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
