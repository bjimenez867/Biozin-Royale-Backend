namespace Biozin_Royale_Backend.Dominio.Entities;

public class SecurityEvent
{
    public Guid Id { get; set; }
    // Igual que Session: un evento pertenece a un Profile o a un StaffMember, nunca ambos.
    public Guid? ProfileId { get; set; }
    public Guid? StaffId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
