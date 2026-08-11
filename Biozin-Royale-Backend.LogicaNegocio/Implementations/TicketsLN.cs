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

    public async Task<Response<TTicketResultado>> CrearTicketAsync(TCrearTicket datos, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        if (string.IsNullOrWhiteSpace(datos.Subject) || string.IsNullOrWhiteSpace(datos.Description))
        {
            resultado.lpError("Datos inválidos", "El asunto y la descripción son obligatorios.");
            return resultado;
        }

        var agentes = await _unitOfWork.StaffMembers.ObtenerEntidadesAsync(s => s.Status == "active");

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
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = MapTicket(ticket, asignadoNombre, null, null, null);
        return resultado;
    }

    public async Task<Response<IEnumerable<TTicketResultado>>> ListarTicketsUsuarioAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TTicketResultado>>();

        var tickets = (await _unitOfWork.SupportTickets
            .ObtenerEntidadesAsync(t => t.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        var staffIds = tickets.Where(t => t.AssignedTo.HasValue).Select(t => t.AssignedTo!.Value).Distinct().ToList();
        var staffDict = staffIds.Count > 0
            ? (await _unitOfWork.StaffMembers.ObtenerEntidadesAsync(s => staffIds.Contains(s.Id)))
                .ToDictionary(s => s.Id, s => s.DisplayName)
            : new Dictionary<Guid, string>();

        resultado.ReturnValue = tickets.Select(t => MapTicket(
            t,
            t.AssignedTo.HasValue && staffDict.TryGetValue(t.AssignedTo.Value, out var n) ? n : null,
            null, null, null
        ));

        return resultado;
    }

    public async Task<Response<IEnumerable<TTicketResultado>>> ListarTodosAsync()
    {
        var resultado = new Response<IEnumerable<TTicketResultado>>();

        var tickets = (await _unitOfWork.SupportTickets.ListarAsync())
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        var staffIds = tickets.Where(t => t.AssignedTo.HasValue).Select(t => t.AssignedTo!.Value).Distinct().ToList();
        var staffDict = staffIds.Count > 0
            ? (await _unitOfWork.StaffMembers.ObtenerEntidadesAsync(s => staffIds.Contains(s.Id)))
                .ToDictionary(s => s.Id, s => s.DisplayName)
            : new Dictionary<Guid, string>();

        var userIds = tickets.Select(t => t.UserId).Distinct().ToList();
        var perfiles = userIds.Count > 0
            ? (await _unitOfWork.Profiles.ObtenerEntidadesAsync(p => userIds.Contains(p.UserId)))
                .ToDictionary(p => p.UserId)
            : new Dictionary<Guid, Profile>();

        resultado.ReturnValue = tickets.Select(t =>
        {
            perfiles.TryGetValue(t.UserId, out var perfil);
            staffDict.TryGetValue(t.AssignedTo ?? Guid.Empty, out var staffNombre);
            return MapTicket(t, staffNombre, perfil?.DisplayName, perfil?.Email, perfil?.Username);
        });

        return resultado;
    }

    public async Task<Response<TTicketResultado>> ObtenerTicketAsync(Guid ticketId, Guid callerId, string callerRole)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        if (callerRole == "user" || callerRole == "authenticated")
        {
            if (ticket.UserId != callerId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
                return resultado;
            }
        }

        string? staffNombre = null;
        if (ticket.AssignedTo.HasValue)
        {
            staffNombre = (await _unitOfWork.StaffMembers
                .ObtenerEntidadAsync(s => s.Id == ticket.AssignedTo.Value))?.DisplayName;
        }

        Profile? perfil = null;
        if (callerRole != "user" && callerRole != "authenticated")
        {
            perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == ticket.UserId);
        }

        resultado.ReturnValue = MapTicket(ticket, staffNombre, perfil?.DisplayName, perfil?.Email, perfil?.Username);
        return resultado;
    }

    // ── Mensajes ───────────────────────────────────────────────────────────

    public async Task<Response<IEnumerable<TMessage>>> ListarMensajesAsync(Guid ticketId, Guid callerId, string callerRole)
    {
        var resultado = new Response<IEnumerable<TMessage>>();

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        if (callerRole == "user" || callerRole == "authenticated")
        {
            if (ticket.UserId != callerId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
                return resultado;
            }
        }

        var mensajes = await _unitOfWork.TicketMessages.ObtenerEntidadesAsync(m => m.TicketId == ticketId);

        resultado.ReturnValue = mensajes
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
            });

        return resultado;
    }

    public async Task<Response<TMessage>> EnviarMensajeAsync(Guid ticketId, Guid senderId, string senderRole, TEnviarMensaje datos)
    {
        var resultado = new Response<TMessage>();

        if (string.IsNullOrWhiteSpace(datos.Body))
        {
            resultado.lpError("Vacío", "El mensaje no puede estar vacío.");
            return resultado;
        }

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        if (senderRole == "user" || senderRole == "authenticated")
        {
            if (ticket.UserId != senderId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
                return resultado;
            }
            if (ticket.Status == "resuelto" || ticket.Status == "cerrado")
            {
                resultado.lpError("Ticket cerrado", "El ticket está cerrado. Reabre el ticket para continuar la conversación.");
                return resultado;
            }
        }

        string senderName;
        if (senderRole == "user" || senderRole == "authenticated")
        {
            var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == senderId);
            senderName = perfil?.DisplayName ?? perfil?.Username ?? "Usuario";
        }
        else
        {
            var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == senderId);
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
        await _unitOfWork.CompletarAsync();

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

        return resultado;
    }

    // ── Gestión ────────────────────────────────────────────────────────────

    public async Task<Response<TTicketResultado>> AsignarTicketAsync(Guid ticketId, Guid staffMemberId)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == staffMemberId);
        if (staff == null)
        {
            resultado.lpError("No encontrado", "El agente no existe.");
            return resultado;
        }

        ticket.AssignedTo = staffMemberId;
        ticket.UpdatedAt  = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = MapTicket(ticket, staff.DisplayName, null, null, null);
        return resultado;
    }

    public async Task<Response<TTicketResultado>> CambiarEstadoAsync(Guid ticketId, string status)
    {
        var resultado = new Response<TTicketResultado>();

        var allowed = new[] { "nuevo", "en_proceso", "resuelto" };
        if (!allowed.Contains(status))
        {
            resultado.lpError("Estado inválido", $"El estado debe ser: {string.Join(", ", allowed)}.");
            return resultado;
        }

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        ticket.Status    = status;
        ticket.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.SupportTickets.Modificar(ticket);
        await _unitOfWork.CompletarAsync();

        string? staffNombre = null;
        if (ticket.AssignedTo.HasValue)
            staffNombre = (await _unitOfWork.StaffMembers
                .ObtenerEntidadAsync(s => s.Id == ticket.AssignedTo.Value))?.DisplayName;

        resultado.ReturnValue = MapTicket(ticket, staffNombre, null, null, null);
        return resultado;
    }

    public async Task<Response<IEnumerable<TStaffSimple>>> ListarAgentesAsync()
    {
        var resultado = new Response<IEnumerable<TStaffSimple>>();

        var staff = await _unitOfWork.StaffMembers
            .ObtenerEntidadesAsync(s => s.Status == "active" && s.Role == "soporte");

        resultado.ReturnValue = staff
            .Select(s => new TStaffSimple { Id = s.Id, DisplayName = s.DisplayName, Role = s.Role });

        return resultado;
    }

    // ── Reabrir / Valorar ──────────────────────────────────────────────────

    public async Task<Response<TTicketResultado>> ReopenAsync(Guid ticketId, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        if (ticket.UserId != userId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return resultado;
        }

        if (ticket.Status != "resuelto")
        {
            resultado.lpError("No aplica", "Solo se pueden reabrir tickets resueltos.");
            return resultado;
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

        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = MapTicket(ticket, null, null, null, null);
        return resultado;
    }

    public async Task<Response<TTicketResultado>> RateAsync(Guid ticketId, Guid userId, short rating)
    {
        var resultado = new Response<TTicketResultado>();

        if (rating < 1 || rating > 5)
        {
            resultado.lpError("Valoración inválida", "La valoración debe estar entre 1 y 5.");
            return resultado;
        }

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        if (ticket.UserId != userId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return resultado;
        }

        if (ticket.Status != "resuelto")
        {
            resultado.lpError("No aplica", "Solo se pueden valorar tickets resueltos.");
            return resultado;
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

        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = MapTicket(ticket, null, null, null, null);
        return resultado;
    }

    public async Task<Response<TTicketResultado>> CerrarAsync(Guid ticketId, Guid userId)
    {
        var resultado = new Response<TTicketResultado>();

        var ticket = await _unitOfWork.SupportTickets.ObtenerEntidadAsync(t => t.Id == ticketId);
        if (ticket == null)
        {
            resultado.lpError("No encontrado", "El ticket no existe.");
            return resultado;
        }

        if (ticket.UserId != userId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a este ticket.");
            return resultado;
        }

        if (ticket.Status != "resuelto")
        {
            resultado.lpError("No aplica", "Solo se pueden cerrar definitivamente tickets resueltos.");
            return resultado;
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

        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = MapTicket(ticket, null, null, null, null);
        return resultado;
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
        var profile = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.Email == fromEmail);

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
        await _unitOfWork.CompletarAsync();

        respuesta.ReturnValue = MapTicket(ticket, null, profile.DisplayName, profile.Email, profile.Username);
        return respuesta;
    }
}
