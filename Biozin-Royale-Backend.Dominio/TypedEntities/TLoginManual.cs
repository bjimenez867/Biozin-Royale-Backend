using System.ComponentModel.DataAnnotations;

namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TLoginManual
{
    [EmailAddress(ErrorMessage = "El correo electrónico no tiene un formato válido.")]
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
