namespace Biozin_Royale_Backend.Dominio.Entities;

public class InternalRequestMessage
{
    public Guid Id { get; set; }
    public Guid InternalRequestId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
