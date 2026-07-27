namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TSession
{
    public Guid Id { get; set; }
    public string? DeviceLabel { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCurrent { get; set; }
}
