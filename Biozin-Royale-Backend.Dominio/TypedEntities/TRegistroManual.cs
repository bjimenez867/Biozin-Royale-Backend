using System.ComponentModel.DataAnnotations;

namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TRegistroManual
{
    public string Nombre { get; set; } = string.Empty;
    [EmailAddress(ErrorMessage = "El correo electrónico no tiene un formato válido.")]
    public string Email { get; set; } = string.Empty;
    [RegularExpression(@"^\+[1-9]\d{6,14}$", ErrorMessage = "El teléfono debe estar en formato internacional (ej. +50681447441).")]
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Confirm { get; set; } = string.Empty;
}
