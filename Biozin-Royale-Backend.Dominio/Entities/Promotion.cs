namespace Biozin_Royale_Backend.Dominio.Entities;

public class Promotion
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string PromotionType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal Amount { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
