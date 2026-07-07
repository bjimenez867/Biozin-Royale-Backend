using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IAuthLN
{
    Task<Response<TPerfilResultado>> RegistrarManualAsync(TRegistroManual datos);
    Task<Response<TPerfilResultado>> LoginManualAsync(string email, string password);
    Task<Response<TPerfilResultado>> SincronizarOAuthAsync(Guid supabaseUserId, string? email, string? nombreCompleto, bool esAnonimo);
    Task<Response<TPerfilResultado>> ReclamarInvitadoAsync(Guid userId, TRegistroManual datos);
    Task<Response<bool>> SolicitarRecuperacionAsync(string email);
    Task<Response<bool>> RestablecerPasswordAsync(string email, string code, string newPassword);
    Task<Response<bool>> EnviarVerificacionAsync(string email);
    Task<Response<bool>> VerificarEmailAsync(string email, string code);
}