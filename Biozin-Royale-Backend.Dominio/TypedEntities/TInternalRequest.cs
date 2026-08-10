namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TInternalRequest
{
    public Guid Id { get; set; }
    public int RequestNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid RequestedBy { get; set; }
    public string? RequestedByName { get; set; }
    public Guid TargetAdminId { get; set; }
    public string? TargetAdminName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
