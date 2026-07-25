using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface ITicketsLN
{
    Task<Response<TTicketResultado>> CrearTicketAsync(TCrearTicket datos, Guid userId);
    Task<Response<IEnumerable<TTicketResultado>>> ListarTicketsUsuarioAsync(Guid userId);
    Task<Response<IEnumerable<TTicketResultado>>> ListarTodosAsync();
    Task<Response<TTicketResultado>> ObtenerTicketAsync(Guid ticketId, Guid callerId, string callerRole);
}
