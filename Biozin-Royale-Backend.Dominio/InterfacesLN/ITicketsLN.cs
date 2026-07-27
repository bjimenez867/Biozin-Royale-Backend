using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface ITicketsLN
{
    Task<Response<TTicketResultado>> CrearTicketAsync(TCrearTicket datos, Guid userId);
    Task<Response<IEnumerable<TTicketResultado>>> ListarTicketsUsuarioAsync(Guid userId);
    Task<Response<IEnumerable<TTicketResultado>>> ListarTodosAsync();
    Task<Response<TTicketResultado>> ObtenerTicketAsync(Guid ticketId, Guid callerId, string callerRole);

    Task<Response<IEnumerable<TMessage>>> ListarMensajesAsync(Guid ticketId, Guid callerId, string callerRole);
    Task<Response<TMessage>> EnviarMensajeAsync(Guid ticketId, Guid senderId, string senderRole, TEnviarMensaje datos);
    Task<Response<TTicketResultado>> AsignarTicketAsync(Guid ticketId, Guid staffMemberId);
    Task<Response<TTicketResultado>> CambiarEstadoAsync(Guid ticketId, string status);
    Task<Response<IEnumerable<TStaffSimple>>> ListarAgentesAsync();
    Task<Response<TTicketResultado>> ReopenAsync(Guid ticketId, Guid userId);
    Task<Response<TTicketResultado>> RateAsync(Guid ticketId, Guid userId, short rating);
}
