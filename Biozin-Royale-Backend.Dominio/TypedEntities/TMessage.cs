namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TMessage
{
    public Guid Id { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
    public DateTime CreatedAt { get; set; }
}
