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

    // ── Tickets ────────────────────────────────────────────────────────────

    public Task<Response<TTicketResultado>> CrearTicketAsync(TCrearTicket datos, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        if (string.IsNullOrWhiteSpace(datos.Subject) || string.IsNullOrWhiteSpace(datos.Description))
        {
            resultado.lpError("Datos inválidos", "El asunto y la descripción son obligatorios.");
            return Task.FromResult(resultado);
        }

        var agentes = _unitOfWork.StaffMembers
            .ObtenerEntidades(s => s.Status == "active")
            .ReturnValue?
            .ToList() ?? new List<StaffMember>();

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

        var staffIds = tickets.Where(t => t.AssignedTo.HasValue).Select(t => t.AssignedTo!.Value).Distinct().ToList();
        var staffDict = staffIds.Count > 0
            ? _unitOfWork.StaffMembers.ObtenerEntidades(s => staffIds.Contains(s.Id)).ReturnValue?
                .ToDictionary(s => s.Id, s => s.DisplayName) ?? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string>();

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

        if (callerRole == "user" || callerRole == "authenticated")
        {
            if (ticket.UserId != callerId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
                return Task.FromResult(resultado);
            }
        }

        string? staffNombre = null;
        if (ticket.AssignedTo.HasValue)
        {
            staffNombre = _unitOfWork.StaffMembers
                .ObtenerEntidad(s => s.Id == ticket.AssignedTo.Value).ReturnValue?.DisplayName;
        }

        Profile? perfil = null;
        if (callerRole != "user" && callerRole != "authenticated")
        {
            perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == ticket.UserId).ReturnValue;
        }

        resultado.ReturnValue = MapTicket(ticket, staffNombre, perfil?.DisplayName, perfil?.Email, perfil?.Username);
        return Task.FromResult(resultado);
    }

    // ── Mensajes ───────────────────────────────────────────────────────────

    public Task<Response<IEnumerable<TMessage>>> ListarMensajesAsync(Guid ticketId, Guid callerId, string callerRole)
    {
        var resultado = new Response<IEnumerable<TMessage>>();

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        if (callerRole == "user" || callerRole == "authenticated")
        {
            if (ticket.UserId != callerId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
                return Task.FromResult(resultado);
            }
        }

        var mensajes = _unitOfWork.TicketMessages
            .ObtenerEntidades(m => m.TicketId == ticketId)
            .ReturnValue?
            .OrderBy(m => m.CreatedAt)
            .Select(m => new TMessage
            {
                Id         = m.Id,
                SenderName = m.SenderName,
                SenderRole = m.SenderRole,
                Body       = m.Body,
                FileUrl    = m.FileUrl,
                FileName   = m.FileName,
                CreatedAt  = m.CreatedAt,
            }) ?? Enumerable.Empty<TMessage>();

        resultado.ReturnValue = mensajes;
        return Task.FromResult(resultado);
    }

    public Task<Response<TMessage>> EnviarMensajeAsync(Guid ticketId, Guid senderId, string senderRole, TEnviarMensaje datos)
    {
        var resultado = new Response<TMessage>();

        if (string.IsNullOrWhiteSpace(datos.Body))
        {
            resultado.lpError("Vacío", "El mensaje no puede estar vacío.");
            return Task.FromResult(resultado);
        }

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        if (senderRole == "user" || senderRole == "authenticated")
        {
            if (ticket.UserId != senderId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
                return Task.FromResult(resultado);
            }
            if (ticket.Status == "resuelto" || ticket.Status == "cerrado")
            {
                resultado.lpError("Ticket cerrado", "El ticket está cerrado. Reabre el ticket para continuar la conversación.");
                return Task.FromResult(resultado);
            }
        }

        string senderName;
        if (senderRole == "user" || senderRole == "authenticated")
        {
            var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == senderId).ReturnValue;
            senderName = perfil?.DisplayName ?? perfil?.Username ?? "Usuario";
        }
        else
        {
            var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == senderId).ReturnValue;
            senderName = staff?.DisplayName ?? "Soporte";
        }

        var mensaje = new TicketMessage
        {
            Id         = Guid.NewGuid(),
            TicketId   = ticketId,
            SenderId   = senderId,
            SenderRole = senderRole,
            SenderName = senderName,
            Body       = datos.Body.Trim(),
            FileUrl    = datos.FileUrl,
            FileName   = datos.FileName,
            CreatedAt  = DateTime.UtcNow,
        };

        _unitOfWork.TicketMessages.Insertar(mensaje);

        // Staff reply moves ticket to en_proceso; any message bumps updated_at
        if ((senderRole == "soporte" || senderRole == "admin") && ticket.Status == "nuevo")
            ticket.Status = "en_proceso";

        ticket.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TMessage
        {
            Id         = mensaje.Id,
            SenderName = senderName,
            SenderRole = senderRole,
            Body       = mensaje.Body,
            FileUrl    = mensaje.FileUrl,
            FileName   = mensaje.FileName,
            CreatedAt  = mensaje.CreatedAt,
        };

        return Task.FromResult(resultado);
    }

    // ── Gestión ────────────────────────────────────────────────────────────

    public Task<Response<TTicketResultado>> AsignarTicketAsync(Guid ticketId, Guid staffMemberId)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffMemberId).ReturnValue;
        if (staff == null)
        {
            resultado.lpError("No encontrado", "El agente no existe.");
            return Task.FromResult(resultado);
        }

        ticket.AssignedTo = staffMemberId;
        ticket.UpdatedAt  = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);
        _unitOfWork.Completar();

        resultado.ReturnValue = MapTicket(ticket, staff.DisplayName, null, null, null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TTicketResultado>> CambiarEstadoAsync(Guid ticketId, string status)
    {
        var resultado = new Response<TTicketResultado>();

        var allowed = new[] { "nuevo", "en_proceso", "resuelto" };
        if (!allowed.Contains(status))
        {
            resultado.lpError("Estado inválido", $"El estado debe ser: {string.Join(", ", allowed)}.");
            return Task.FromResult(resultado);
        }

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        ticket.Status    = status;
        ticket.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);
        _unitOfWork.Completar();

        string? staffNombre = null;
        if (ticket.AssignedTo.HasValue)
            staffNombre = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == ticket.AssignedTo.Value).ReturnValue?.DisplayName;

        resultado.ReturnValue = MapTicket(ticket, staffNombre, null, null, null);
        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TStaffSimple>>> ListarAgentesAsync()
    {
        var resultado = new Response<IEnumerable<TStaffSimple>>();

        var staff = _unitOfWork.StaffMembers
            .ObtenerEntidades(s => s.Status == "active" && s.Role == "soporte")
            .ReturnValue?
            .Select(s => new TStaffSimple { Id = s.Id, DisplayName = s.DisplayName, Role = s.Role })
            ?? Enumerable.Empty<TStaffSimple>();

        resultado.ReturnValue = staff;
        return Task.FromResult(resultado);
    }

    // ── Reabrir / Valorar ──────────────────────────────────────────────────

    public Task<Response<TTicketResultado>> ReopenAsync(Guid ticketId, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        if (ticket.UserId != userId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return Task.FromResult(resultado);
        }

        if (ticket.Status != "resuelto")
        {
            resultado.lpError("No aplica", "Solo se pueden reabrir tickets resueltos.");
            return Task.FromResult(resultado);
        }

        ticket.Status    = "en_proceso";
        ticket.Rating    = null;
        ticket.RatedAt   = null;
        ticket.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);

        _unitOfWork.TicketMessages.Insertar(new TicketMessage
        {
            Id         = Guid.NewGuid(),
            TicketId   = ticketId,
            SenderId   = userId,
            SenderRole = "system",
            SenderName = "Sistema",
            Body       = "El usuario reabrió el ticket.",
            CreatedAt  = DateTime.UtcNow,
        });

        _unitOfWork.Completar();

        resultado.ReturnValue = MapTicket(ticket, null, null, null, null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TTicketResultado>> RateAsync(Guid ticketId, Guid userId, short rating)
    {
        var resultado = new Response<TTicketResultado>();

        if (rating < 1 || rating > 5)
        {
            resultado.lpError("Valoración inválida", "La valoración debe estar entre 1 y 5.");
            return Task.FromResult(resultado);
        }

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        if (ticket.UserId != userId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return Task.FromResult(resultado);
        }

        if (ticket.Status != "resuelto")
        {
            resultado.lpError("No aplica", "Solo se pueden valorar tickets resueltos.");
            return Task.FromResult(resultado);
        }

        ticket.UpdatedAt = DateTime.UtcNow;

        if (rating >= 4)
        {
            // Buena experiencia → cierre definitivo
            ticket.Rating  = rating;
            ticket.RatedAt = DateTime.UtcNow;
            ticket.Status  = "cerrado";
            _unitOfWork.SupportTickets.Modificar(ticket);
            _unitOfWork.TicketMessages.Insertar(new TicketMessage
            {
                Id         = Guid.NewGuid(),
                TicketId   = ticketId,
                SenderId   = userId,
                SenderRole = "system",
                SenderName = "Sistema",
                Body       = "El usuario valoró la atención y el ticket fue cerrado.",
                CreatedAt  = DateTime.UtcNow,
            });
        }
        else if (rating <= 2)
        {
            // Mala experiencia → reabrir automáticamente (sin guardar rating)
            ticket.Status  = "en_proceso";
            ticket.Rating  = null;
            ticket.RatedAt = null;
            _unitOfWork.SupportTickets.Modificar(ticket);
            _unitOfWork.TicketMessages.Insertar(new TicketMessage
            {
                Id         = Guid.NewGuid(),
                TicketId   = ticketId,
                SenderId   = userId,
                SenderRole = "system",
                SenderName = "Sistema",
                Body       = "El ticket fue reabierto por baja satisfacción del usuario.",
                CreatedAt  = DateTime.UtcNow,
            });
        }
        else
        {
            // Rating 3 → guardar valoración, el usuario elige qué hacer
            ticket.Rating  = rating;
            ticket.RatedAt = DateTime.UtcNow;
            _unitOfWork.SupportTickets.Modificar(ticket);
        }

        _unitOfWork.Completar();

        resultado.ReturnValue = MapTicket(ticket, null, null, null, null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TTicketResultado>> CerrarAsync(Guid ticketId, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = _unitOfWork.SupportTickets.ObtenerEntidad(t => t.Id == ticketId).ReturnValue;
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return Task.FromResult(resultado);
        }

        if (ticket.UserId != userId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return Task.FromResult(resultado);
        }

        if (ticket.Status != "resuelto")
        {
            resultado.lpError("No aplica", "Solo se pueden cerrar definitivamente tickets resueltos.");
            return Task.FromResult(resultado);
        }

        ticket.Status    = "cerrado";
        ticket.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);

        _unitOfWork.TicketMessages.Insertar(new TicketMessage
        {
            Id         = Guid.NewGuid(),
            TicketId   = ticketId,
            SenderId   = userId,
            SenderRole = "system",
            SenderName = "Sistema",
            Body       = "El ticket fue cerrado definitivamente.",
            CreatedAt  = DateTime.UtcNow,
        });

        _unitOfWork.Completar();

        resultado.ReturnValue = MapTicket(ticket, null, null, null, null);
        return Task.FromResult(resultado);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static TTicketResultado MapTicket(
        SupportTicket t,
        string? assignedToName,
        string? userDisplayName,
        string? userEmail,
        string? userUsername)
    {
        return new TTicketResultado
        {
            Id              = t.Id,
            TicketNumber    = t.TicketNumber,
            Subject         = t.Subject,
            Category        = t.Category,
            Priority        = t.Priority,
            Status          = t.Status,
            Description     = t.Description,
            AssignedTo      = t.AssignedTo,
            AssignedToName  = assignedToName,
            CreatedAt       = t.CreatedAt,
            UpdatedAt       = t.UpdatedAt,
            UserDisplayName = userDisplayName,
            UserEmail       = userEmail,
            UserUsername    = userUsername,
            Rating          = t.Rating,
        };
    }

    public async Task<Response<TTicketResultado>> CrearDesdeEmailAsync(
        string fromEmail, string fromName, string category, string body)
    {
        var respuesta = new Response<TTicketResultado>();

        // Buscar usuario registrado por correo
        var profile = _unitOfWork.Profiles
            .ObtenerEntidades(p => p.Email == fromEmail)
            .ReturnValue?.FirstOrDefault();

        if (profile is null)
        {
            respuesta.blnError = true;
            respuesta.strResponseMessage = "No se encontró un usuario registrado con ese correo.";
            return respuesta;
        }

        var ticket = new SupportTicket
        {
            Id          = Guid.NewGuid(),
            UserId      = profile.UserId,
            Subject     = $"[Email] {category} - {fromName}",
            Category    = category,
            Priority    = "normal",
            Status      = "nuevo",
            Description = string.IsNullOrWhiteSpace(body) ? "(Sin descripción)" : body,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _unitOfWork.SupportTickets.Insertar(ticket);
        _unitOfWork.Completar();

        respuesta.ReturnValue = MapTicket(ticket, null, profile.DisplayName, profile.Email, profile.Username);
        return respuesta;
    }
}
