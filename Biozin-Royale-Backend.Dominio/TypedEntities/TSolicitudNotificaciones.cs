namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TNuevaSolicitudNotif
{
    public Guid Id { get; set; }
    public int RequestNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? RequestedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TNuevoMensajeSolicitudNotif
{
    public Guid SolicitudId { get; set; }
    public int RequestNumber { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
