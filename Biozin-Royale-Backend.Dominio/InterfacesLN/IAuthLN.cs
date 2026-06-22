using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IAuthLN
{
    Task<Response<TPerfilResultado>> RegistrarManualAsync(TRegistroManual datos);
    Task<Response<TPerfilResultado>> LoginManualAsync(string email, string password);
    Task<Response<TPerfilResultado>> SincronizarOAuthAsync(Guid supabaseUserId, string email, string? nombreCompleto);
}