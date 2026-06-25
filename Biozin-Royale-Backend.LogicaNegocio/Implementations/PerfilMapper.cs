using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

// Compartido por AuthLN (register/login/sync) y ProfileLN (get/update perfil): ambos
// necesitan mapear la misma entidad Profile al mismo DTO de respuesta.
internal static class PerfilMapper
{
    public static TPerfilResultado MapearPerfil(Profile perfil, string? token)
    {
        var camposPendientes = new List<string>();
        if (string.IsNullOrWhiteSpace(perfil.Phone)) camposPendientes.Add("phone");
        if (string.IsNullOrWhiteSpace(perfil.Country)) camposPendientes.Add("country");
        if (perfil.Birthdate is null) camposPendientes.Add("birthdate");

        return new TPerfilResultado
        {
            Id = perfil.UserId,
            Username = perfil.Username,
            DisplayName = perfil.DisplayName,
            Email = perfil.Email,
            Phone = perfil.Phone,
            Country = perfil.Country,
            Birthdate = perfil.Birthdate,
            Status = perfil.Status,
            Role = perfil.Role,
            IsGuest = perfil.IsGuest,
            Token = token,
            CamposPendientes = camposPendientes
        };
    }
}
