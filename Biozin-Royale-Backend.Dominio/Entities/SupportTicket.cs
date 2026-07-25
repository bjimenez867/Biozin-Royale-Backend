namespace Biozin_Royale_Backend.Dominio.Entities;

public class SupportTicket
{
    public Guid Id { get; set; }
    public int TicketNumber { get; set; }
    public Guid UserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = "nuevo";
    public string Description { get; set; } = string.Empty;
    public Guid? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
