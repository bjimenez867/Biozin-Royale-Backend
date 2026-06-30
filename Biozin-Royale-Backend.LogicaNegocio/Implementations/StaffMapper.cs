using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

// Mapea StaffMember al mismo TPerfilResultado que usa AuthLN/ProfileLN: el frontend
// (AuthService, roleGuard, currentProfile) reconoce una sesión de staff sin cambios.
internal static class StaffMapper
{
    public static TPerfilResultado MapearComoPerfil(StaffMember staff, string? token, string? tempPassword = null)
    {
        return new TPerfilResultado
        {
            Id = staff.Id,
            Username = staff.Username,
            DisplayName = staff.DisplayName,
            Email = staff.Email,
            Phone = staff.Phone,
            Country = null,
            Birthdate = null,
            Status = staff.Status,
            Role = staff.Role,
            IsGuest = false,
            Token = token,
            CamposPendientes = new List<string>(),
            TempPassword = tempPassword
        };
    }
}
