namespace Biozin_Royale_Backend.Dominio.Entities;

public class SecurityEvent
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
