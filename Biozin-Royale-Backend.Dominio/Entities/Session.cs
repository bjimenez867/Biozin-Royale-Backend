namespace Biozin_Royale_Backend.Dominio.Entities;

public class Session
{
    public Guid Id { get; set; }
    // Una sesión pertenece a un Profile (jugador) o a un StaffMember (admin/soporte),
    // nunca ambos — ver StaffLN.GenerarTokenConSesion vs AuthLN.GenerarTokenConSesion.
    public Guid? ProfileId { get; set; }
    public Guid? StaffId { get; set; }
    public string? DeviceLabel { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
}
