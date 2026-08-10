using System.ComponentModel.DataAnnotations;

namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TCrearStaffMember
{
    public string Nombre { get; set; } = string.Empty;
    [EmailAddress(ErrorMessage = "El correo de contacto no tiene un formato válido.")]
    public string CorreoContacto { get; set; } = string.Empty;
    [RegularExpression(@"^\+[1-9]\d{6,14}$", ErrorMessage = "El teléfono debe estar en formato internacional (ej. +50681447441).")]
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
}
