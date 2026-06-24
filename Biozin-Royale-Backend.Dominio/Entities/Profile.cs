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
    public decimal Balance { get; set; } = 0m;
}