using System.ComponentModel.DataAnnotations;

namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TActualizarStaffMember
{
    public string? DisplayName { get; set; }
    [RegularExpression(@"^\+[1-9]\d{6,14}$", ErrorMessage = "El teléfono debe estar en formato internacional (ej. +50681447441).")]
    public string? Phone { get; set; }
}
