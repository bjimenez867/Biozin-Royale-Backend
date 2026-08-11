using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;
using Microsoft.Extensions.Configuration;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class AvatarLN : IAvatarLN
{
    private readonly IUnitWork _unitOfWork;
    private readonly string _baseUrl;

    public AvatarLN(IUnitWork unitOfWork, IConfiguration config)
    {
        _unitOfWork = unitOfWork;
        _baseUrl = config["Supabase:AvatarsBucketBaseUrl"] ?? "";
    }

    public Response<IEnumerable<TAvatarResultado>> ListarAvatars()
    {
        var resultado = new Response<IEnumerable<TAvatarResultado>>();
        var avatares = _unitOfWork.Avatars
            .ObtenerEntidades(a => a.IsActive)
            .ReturnValue ?? [];

        resultado.ReturnValue = avatares
            .OrderBy(a => a.SortOrder)
            .Select(a => new TAvatarResultado
            {
                Id          = a.Id,
                Name        = a.Name,
                Url         = $"{_baseUrl}/{a.StoragePath}",
                Description = a.Description,
                SortOrder   = a.SortOrder,
            });

        return resultado;
    }

    public async Task<Response<bool>> ActualizarAvatarAsync(Guid userId, int avatarId)
    {
        var resultado = new Response<bool>();

        var avatar = await _unitOfWork.Avatars.ObtenerEntidadAsync(a => a.Id == avatarId && a.IsActive);
        if (avatar is null)
        {
            resultado.lpError("Avatar no encontrado", "El avatar seleccionado no existe.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No se encontró el perfil del usuario.");
            return resultado;
        }

        perfil.AvatarId  = avatarId;
        perfil.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Profiles.Modificar(perfil);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }
}
