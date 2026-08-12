using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Biozin_Royale_Backend.Dominio.InterfacesAD;

namespace Biozin_Royale_Backend.API.Hubs;

/// Canal en tiempo real de los chats de soporte: tickets (usuario↔soporte) y
/// solicitudes internas (soporte↔admin). Este hub solo gestiona la membresía a
/// los grupos — los eventos los emiten los controllers REST después de
/// persistir, para que la API HTTP siga siendo la única fuente de verdad.
[Authorize]
public class ChatHub : Hub
{
    private readonly IUnitWork _unitOfWork;

    public ChatHub(IUnitWork unitOfWork) => _unitOfWork = unitOfWork;

    private Guid UserId =>
        Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new HubException("Sesión inválida.");

    private string Role => Context.User?.FindFirst("role")?.Value ?? "user";
    private bool EsStaff => Role is "admin" or "soporte";

    public static string TicketGroup(Guid id) => $"ticket:{id}";
    public static string SolicitudGroup(Guid id) => $"solicitud:{id}";

    // ── Tickets (usuario ↔ soporte) ──────────────────────────────────────────

    public async Task JoinTicket(Guid ticketId)
    {
        // El usuario solo puede escuchar sus propios tickets; el staff cualquiera
        if (!EsStaff)
        {
            var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId)
                ?? throw new HubException("El ticket no existe.");
            if (ticket.UserId != UserId)
                throw new HubException("No tienes acceso a este ticket.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
    }

    public Task LeaveTicket(Guid ticketId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(ticketId));

    // ── Solicitudes internas (soporte ↔ admin) ───────────────────────────────

    public async Task JoinSolicitud(Guid solicitudId)
    {
        if (!EsStaff)
            throw new HubException("Sin acceso.");

        // Soporte solo puede escuchar sus propias solicitudes; admin cualquiera
        if (Role == "soporte")
        {
            var solicitud = await _unitOfWork.InternalRequests
                .ObtenerEntidadAsync(r => r.Id == solicitudId)
                ?? throw new HubException("La solicitud no existe.");
            if (solicitud.RequestedBy != UserId)
                throw new HubException("No tienes acceso a esta solicitud.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, SolicitudGroup(solicitudId));
    }

    public Task LeaveSolicitud(Guid solicitudId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, SolicitudGroup(solicitudId));
}
