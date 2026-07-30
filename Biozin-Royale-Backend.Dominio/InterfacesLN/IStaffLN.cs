using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IStaffLN
{
    Task<Response<TPerfilResultado>> CrearMiembroAsync(TCrearStaffMember datos, Guid creadoPorId);
    Task<Response<IEnumerable<TPerfilResultado>>> ListarMiembrosAsync();
    Task<Response<TPerfilResultado>> LoginAsync(string email, string password, string? userAgent, string? ipAddress);
    Task<Response<TPerfilResultado>> ObtenerMeAsync(Guid staffId);
    Task<Response<TPerfilResultado>> ActualizarMeAsync(Guid staffId, TActualizarStaffMember datos);

    Task<Response<TPerfilResultado>> ObtenerMiembroAsync(Guid id);
    Task<Response<TPerfilResultado>> ActualizarMiembroAsync(Guid id, TActualizarStaffMember datos);
    Task<Response<TPerfilResultado>> CambiarStatusMiembroAsync(Guid id, string nuevoStatus);
    Task<Response<bool>> EliminarMiembroAsync(Guid id);
    Task<Response<bool>> CambiarPasswordAsync(Guid staffId, string oldPassword, string newPassword);
    Task<Response<bool>> RestablecerPasswordStaffAsync(Guid id);
    Task<Response<List<TSession>>> ObtenerSesionesAsync(Guid staffId, Guid? currentSessionId);
    Task<Response<bool>> CerrarSesionAsync(Guid staffId, Guid sessionId);
    Task<Response<bool>> CerrarOtrasSesionesAsync(Guid staffId, Guid currentSessionId);
    Task<Response<List<TSecurityEvent>>> ObtenerHistorialSeguridadAsync(Guid staffId);
}
