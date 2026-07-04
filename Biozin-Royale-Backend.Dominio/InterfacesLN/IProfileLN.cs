using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IProfileLN
{
    Task<Response<TPerfilResultado>> ObtenerPerfilAsync(Guid userId);
    Task<Response<TPerfilResultado>> ActualizarPerfilAsync(Guid userId, TActualizarPerfil datos);
    Task<Response<TEstadisticas>> ObtenerEstadisticasAsync(Guid userId);
    Task<Response<List<TAdminUser>>> ObtenerUsuariosAsync(Guid adminId);
    Task<Response<TUserBlockInfo>> ObtenerBloqueoActivoAsync(Guid adminId, Guid userId);
    Task<Response<bool>> BloquearUsuarioAsync(Guid adminId, Guid userId, TBlockUserRequest datos);
    Task<Response<bool>> DesbloquearUsuarioAsync(Guid adminId, Guid userId);
}
