using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class TicketsLN : ITicketsLN
{
    private readonly IUnitWork _unitOfWork;

    public TicketsLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Response<TTicketResultado>> CrearTicketAsync(TCrearTicket datos, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        if (string.IsNullOrWhiteSpace(datos.Subject) || string.IsNullOrWhiteSpace(datos.Description))
        {
            resultado.lpError("Datos inválidos", "El asunto y la descripción son obligatorios.");
            return Task.FromResult(resultado);
        }

        // Asignar aleatoriamente a un agente de soporte activo
        var agentes = _unitOfWork.StaffMembers
            .ObtenerEntidades(s => s.Status == "active")
            .ReturnValue?
            .ToList() ?? new List<StaffMember>();

        // Filtramos por rol soporte (el campo Role no está en StaffMember directamente,
        // así que usamos todos los activos — el admin no debería recibir tickets)
        Guid? asignadoA = null;
        string? asignadoNombre = null;

        if (agentes.Count > 0)
        {
            var agente = agentes[Random.Shared.Next(agentes.Count)];
            asignadoA = agente.Id;
            asignadoNombre = agente.DisplayName;
        }

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Subject = datos.Subject.Trim(),
            Category = datos.Category.Trim(),
            Description = datos.Description.Trim(),
            Priority = "normal",
            Status = "nuevo",
            AssignedTo = asignadoA,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _unitOfWork.SupportTickets.Insertar(ticket);
        _unitOfWork.Completar();

        resultado.ReturnValue = MapTicket(ticket, asignadoNombre, null, null, null);
        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TTicketResultado>>> ListarTicketsUsuarioAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TTicketResultado>>();

        var tickets = _unitOfWork.SupportTickets
            .ObtenerEntidades(t => t.UserId == userId)
            .ReturnValue?
            .OrderByDescending(t => t.CreatedAt)
            .ToList() ?? new List<SupportTicket>();

        var staffIds = tickets.Where(t => t.AssignedTo.HasValue).Select(t => t.AssignedTo!.Value).Distinct().ToList();
        var staffDict = staffIds.Count > 0
            ? _unitOfWork.StaffMembers.ObtenerEntidades(s => staffIds.Contains(s.Id)).ReturnValue?
                .ToDictionary(s => s.Id, s => s.DisplayName) ?? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string>();

        resultado.ReturnValue = tickets.Select(t => MapTicket(
            t,
            t.AssignedTo.HasValue && staffDict.TryGetValue(t.AssignedTo.Value, out var n) ? n : null,
            null, null, null
        ));

        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TTicketResultado>>> ListarTodosAsync()
    {
        var resultado = new Response<IEnumerable<TTicketResultado>>();

        var tickets = _unitOfWork.SupportTickets.Listar().ReturnValue?
            .OrderByDescending(t => t.CreatedAt)
            .ToList() ?? new List<SupportTicket>();

        // Cargar staff asignados
        var staffIds = tickets.Where(t => t.AssignedTo.HasValue).Select(t => t.AssignedTo!.Value).Distinct().ToList();
        var staffDict = staffIds.Count > 0
            ? _unitOfWork.StaffMembers.ObtenerEntidades(s => staffIds.Contains(s.Id)).ReturnValue?
                .ToDictionary(s => s.Id, s => s.DisplayName) ?? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string>();

        // Cargar perfiles de usuarios
        var userIds = tickets.Select(t => t.UserId).Distinct().ToList();
        var perfiles = userIds.Count > 0
            ? _unitOfWork.Profiles.ObtenerEntidades(p => userIds.Contains(p.UserId)).ReturnValue?
                .ToDictionary(p => p.UserId) ?? new Dictionary<Guid, Profile>()
            : new Dictionary<Guid, Profile>();

        resultado.ReturnValue = tickets.Select(t =>
        {
            perfiles.TryGetValue(t.UserId, out var perfil);
            staffDict.TryGetValue(t.AssignedTo ?? Guid.Empty, out var staffNombre);
            return MapTicket(t, staffNombre, perfil?.DisplayName, perfil?.Email, perfil?.Username);
        });

        return Task.FromResult(resultado);
    }

    public Task<Response<TTicketResultado>> ObtenerTicketAsync(Guid ticketId, Guid callerId, string callerRole)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        // Usuario solo puede ver sus propios tickets
        if (callerRole == "user" && ticket.UserId != callerId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return Task.FromResult(resultado);
        }

        string? staffNombre = null;
        if (ticket.AssignedTo.HasValue)
        {
            staffNombre = _unitOfWork.StaffMembers
                .ObtenerEntidad(s => s.Id == ticket.AssignedTo.Value).ReturnValue?.DisplayName;
        }

        Profile? perfil = null;
        if (callerRole != "user")
        {
            perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == ticket.UserId).ReturnValue;
        }

        resultado.ReturnValue = MapTicket(ticket, staffNombre, perfil?.DisplayName, perfil?.Email, perfil?.Username);
        return Task.FromResult(resultado);
    }

    private static TTicketResultado MapTicket(
        SupportTicket t,
        string? assignedToName,
        string? userDisplayName,
        string? userEmail,
        string? userUsername)
    {
        return new TTicketResultado
        {
            Id = t.Id,
            TicketNumber = t.TicketNumber,
            Subject = t.Subject,
            Category = t.Category,
            Priority = t.Priority,
            Status = t.Status,
            Description = t.Description,
            AssignedTo = t.AssignedTo,
            AssignedToName = assignedToName,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            UserDisplayName = userDisplayName,
            UserEmail = userEmail,
            UserUsername = userUsername,
        };
    }
}
