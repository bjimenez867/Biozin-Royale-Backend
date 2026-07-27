namespace Biozin_Royale_Backend.Dominio.Entities;

public class Session
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string? DeviceLabel { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
