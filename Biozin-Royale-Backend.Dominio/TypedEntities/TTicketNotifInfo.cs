namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TTicketNotifInfo
{
    public Guid UserId { get; set; }
    public int TicketNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
}
