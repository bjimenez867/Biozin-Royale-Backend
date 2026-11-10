namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreNotification
{
    public Guid      Id               { get; set; }
    public Guid      UserId           { get; set; }
    public string    NotificationType { get; set; } = "order";
    public string    Title            { get; set; } = string.Empty;
    public string    Body             { get; set; } = string.Empty;
    public string?   ReferenceType    { get; set; }
    public Guid?     ReferenceId      { get; set; }
    public DateTime? ReadAt           { get; set; }
    public DateTime  CreatedAt        { get; set; }
}
