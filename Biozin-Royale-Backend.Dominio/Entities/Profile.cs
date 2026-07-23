namespace Biozin_Royale_Backend.Dominio.Entities;

public class Profile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int? AvatarId { get; set; }
    public bool IsGuest { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Phone { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Country { get; set; }
    public DateOnly? Birthdate { get; set; }
    public string? Password { get; set; }
    public string? ResetCode { get; set; }
    public DateTime? ResetCodeExpiresAt { get; set; }
    public bool EmailVerified { get; set; } = false;
    public string? VerifyCode { get; set; }
    public DateTime? VerifyCodeExpiresAt { get; set; }
    public string? PinHash { get; set; }
    public bool PinEnabled { get; set; } = false;
}
