namespace Biozin_Royale_Backend.Dominio.Entities;

public class UserBlock
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Guid BlockedBy { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; }
    public DateTime? UnblockedAt { get; set; }
    public Guid? UnblockedBy { get; set; }
    public bool IsActive { get; set; } = true;
}
