namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TNuevoTicketNotif
{
    public Guid Id { get; set; }
    public int TicketNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? UserDisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TNuevoMensajeNotif
{
    public Guid TicketId { get; set; }
    public int TicketNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class TTicketNotificaciones
{
    public List<TNuevoTicketNotif> NuevosTickets { get; set; } = new();
    public List<TNuevoMensajeNotif> NuevosMensajes { get; set; } = new();
    public DateTime ServerTime { get; set; }
}
