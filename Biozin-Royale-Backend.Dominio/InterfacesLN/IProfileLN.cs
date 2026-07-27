using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IProfileLN
{
    Task<Response<TPerfilResultado>> ObtenerPerfilAsync(Guid userId);
    Task<Response<TPerfilResultado>> ActualizarPerfilAsync(Guid userId, TActualizarPerfil datos);
    Task<Response<bool>> CambiarPasswordAsync(Guid userId, string oldPassword, string newPassword);
    Task<Response<bool>> CrearPinAsync(Guid userId, string pin);
    Task<Response<bool>> CambiarPinAsync(Guid userId, string oldPin, string newPin);
    Task<Response<bool>> CambiarEstadoPinAsync(Guid userId, string pin, bool enabled);
    Task<Response<bool>> CambiarEstadoTwoFactorAsync(Guid userId, string password, bool enabled);
    Task<Response<List<TSession>>> ObtenerSesionesAsync(Guid userId, Guid? currentSessionId);
    Task<Response<bool>> CerrarSesionAsync(Guid userId, Guid sessionId);
    Task<Response<bool>> CerrarOtrasSesionesAsync(Guid userId, Guid currentSessionId);
    Task<Response<List<TSecurityEvent>>> ObtenerHistorialSeguridadAsync(Guid userId);
    Task<Response<TEstadisticas>> ObtenerEstadisticasAsync(Guid userId);
    Task<Response<List<TAdminUser>>> ObtenerUsuariosAsync(Guid adminId);
    Task<Response<TUserBlockInfo>> ObtenerBloqueoActivoAsync(Guid adminId, Guid userId);
    Task<Response<bool>> BloquearUsuarioAsync(Guid adminId, Guid userId, TBlockUserRequest datos);
    Task<Response<bool>> DesbloquearUsuarioAsync(Guid adminId, Guid userId);
}
