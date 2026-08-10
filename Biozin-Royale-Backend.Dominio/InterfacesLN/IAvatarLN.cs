using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IAvatarLN
{
    Response<IEnumerable<TAvatarResultado>> ListarAvatars();
    Task<Response<bool>> ActualizarAvatarAsync(Guid userId, int avatarId);
}
