using System.ComponentModel.DataAnnotations;

namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TVerificarCodigo2FA
{
    [EmailAddress(ErrorMessage = "El correo electrónico no tiene un formato válido.")]
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
