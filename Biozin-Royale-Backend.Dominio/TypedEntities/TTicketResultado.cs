namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TTicketResultado
{
    public Guid Id { get; set; }
    public int TicketNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? AssignedTo { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Info del usuario (solo visible para soporte/admin)
    public string? UserDisplayName { get; set; }
    public string? UserEmail { get; set; }
    public string? UserUsername { get; set; }
}
