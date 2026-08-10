namespace Biozin_Royale_Backend.Dominio.Entities;

public class InternalRequest
{
    public Guid Id { get; set; }
    public int RequestNumber { get; set; }
    public Guid RequestedBy { get; set; }
    public Guid TargetAdminId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "nuevo";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
