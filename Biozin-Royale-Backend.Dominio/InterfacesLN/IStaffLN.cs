using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IStaffLN
{
    Task<Response<TPerfilResultado>> CrearMiembroAsync(TCrearStaffMember datos, Guid creadoPorId);
    Task<Response<IEnumerable<TPerfilResultado>>> ListarMiembrosAsync();
    Task<Response<TPerfilResultado>> LoginAsync(string email, string password);
}
