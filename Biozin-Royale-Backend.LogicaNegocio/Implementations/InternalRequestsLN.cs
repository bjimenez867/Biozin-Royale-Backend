using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class InternalRequestsLN : IInternalRequestsLN
{
    private readonly IUnitWork _unitOfWork;

    public InternalRequestsLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ── Solicitudes ────────────────────────────────────────────────────────

    public Task<Response<TInternalRequest>> CrearAsync(TCrearInternalRequest datos, Guid soporteId)
    {
        var resultado = new Response<TInternalRequest>();

        if (string.IsNullOrWhiteSpace(datos.Subject) || string.IsNullOrWhiteSpace(datos.Description))
        {
            resultado.lpError("Datos inválidos", "El asunto y la descripción son obligatorios.");
            return Task.FromResult(resultado);
        }

        var admin = _unitOfWork.StaffMembers
            .ObtenerEntidad(s => s.Id == datos.TargetAdminId && s.Role == "admin" && s.Status == "active")
            .ReturnValue;
        if (admin == null)
        {
            resultado.lpError("No encontrado", "El admin seleccionado no existe o no está activo.");
            return Task.FromResult(resultado);
        }

        var solicitante = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == soporteId).ReturnValue;

        var solicitud = new InternalRequest
        {
            Id            = Guid.NewGuid(),
            RequestedBy   = soporteId,
            TargetAdminId = datos.TargetAdminId,
            Subject       = datos.Subject.Trim(),
            Description   = datos.Description.Trim(),
            Status        = "nuevo",
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        _unitOfWork.InternalRequests.Insertar(solicitud);
        _unitOfWork.Completar();

        resultado.ReturnValue = MapSolicitud(solicitud, solicitante?.DisplayName, admin.DisplayName);
        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TInternalRequest>>> ListarMiasAsync(Guid soporteId)
    {
        var resultado = new Response<IEnumerable<TInternalRequest>>();

        var solicitudes = _unitOfWork.InternalRequests
            .ObtenerEntidades(r => r.RequestedBy == soporteId)
            .ReturnValue?
            .OrderByDescending(r => r.CreatedAt)
            .ToList() ?? new List<InternalRequest>();

        resultado.ReturnValue = MapConNombres(solicitudes);
        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TInternalRequest>>> ListarParaMiAsync()
    {
        var resultado = new Response<IEnumerable<TInternalRequest>>();

        var solicitudes = _unitOfWork.InternalRequests
            .Listar()
            .ReturnValue?
            .OrderByDescending(r => r.CreatedAt)
            .ToList() ?? new List<InternalRequest>();

        resultado.ReturnValue = MapConNombres(solicitudes);
        return Task.FromResult(resultado);
    }

    public Task<Response<TInternalRequest>> ObtenerAsync(Guid id, Guid callerId, string callerRole)
    {
        var resultado = new Response<TInternalRequest>();

        var solicitud = _unitOfWork.InternalRequests.ObtenerEntidad(r => r.Id == id).ReturnValue;
        if (solicitud == null)
        {
            resultado.lpError("No encontrado", "La solicitud no existe.");
            return Task.FromResult(resultado);
        }

        if (callerRole == "soporte" && solicitud.RequestedBy != callerId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a esta solicitud.");
            return Task.FromResult(resultado);
        }

        var solicitante = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == solicitud.RequestedBy).ReturnValue;
        var admin = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == solicitud.TargetAdminId).ReturnValue;

        resultado.ReturnValue = MapSolicitud(solicitud, solicitante?.DisplayName, admin?.DisplayName);
        return Task.FromResult(resultado);
    }

    // ── Mensajes ───────────────────────────────────────────────────────────

    public Task<Response<IEnumerable<TInternalRequestMessage>>> ListarMensajesAsync(Guid id, Guid callerId, string callerRole)
    {
        var resultado = new Response<IEnumerable<TInternalRequestMessage>>();

        var solicitud = _unitOfWork.InternalRequests.ObtenerEntidad(r => r.Id == id).ReturnValue;
        if (solicitud == null)
        {
            resultado.lpError("No encontrado", "La solicitud no existe.");
            return Task.FromResult(resultado);
        }

        if (callerRole == "soporte" && solicitud.RequestedBy != callerId)
        {
            resultado.lpError("Sin permiso", "No tienes acceso a esta solicitud.");
            return Task.FromResult(resultado);
        }

        var mensajes = _unitOfWork.InternalRequestMessages
            .ObtenerEntidades(m => m.InternalRequestId == id)
            .ReturnValue?
            .OrderBy(m => m.CreatedAt)
            .Select(m => new TInternalRequestMessage
            {
                Id         = m.Id,
                SenderName = m.SenderName,
                SenderRole = m.SenderRole,
                Body       = m.Body,
                CreatedAt  = m.CreatedAt,
            }) ?? Enumerable.Empty<TInternalRequestMessage>();

        resultado.ReturnValue = mensajes;
        return Task.FromResult(resultado);
    }

    public Task<Response<TInternalRequestMessage>> EnviarMensajeAsync(Guid id, Guid senderId, string senderRole, TEnviarInternalRequestMensaje datos)
    {
        var resultado = new Response<TInternalRequestMessage>();

        if (string.IsNullOrWhiteSpace(datos.Body))
        {
            resultado.lpError("Vacío", "El mensaje no puede estar vacío.");
            return Task.FromResult(resultado);
        }

        var solicitud = _unitOfWork.InternalRequests.ObtenerEntidad(r => r.Id == id).ReturnValue;
        if (solicitud == null)
        {
            resultado.lpError("No encontrado", "La solicitud no existe.");
            return Task.FromResult(resultado);
        }

        if (senderRole == "soporte")
        {
            if (solicitud.RequestedBy != senderId)
            {
                resultado.lpError("Sin permiso", "No tienes acceso a esta solicitud.");
                return Task.FromResult(resultado);
            }
            if (solicitud.Status == "resuelto" || solicitud.Status == "cerrado")
            {
                resultado.lpError("Solicitud cerrada", "La solicitud está cerrada.");
                return Task.FromResult(resultado);
            }
        }

        var remitente = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == senderId).ReturnValue;
        var senderName = remitente?.DisplayName ?? (senderRole == "admin" ? "Admin" : "Soporte");

        var mensaje = new InternalRequestMessage
        {
            Id                = Guid.NewGuid(),
            InternalRequestId = id,
            SenderId          = senderId,
            SenderRole        = senderRole,
            SenderName        = senderName,
            Body              = datos.Body.Trim(),
            CreatedAt         = DateTime.UtcNow,
        };

        _unitOfWork.InternalRequestMessages.Insertar(mensaje);

        if (senderRole == "admin" && solicitud.Status == "nuevo")
            solicitud.Status = "en_proceso";

        solicitud.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.InternalRequests.Modificar(solicitud);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TInternalRequestMessage
        {
            Id         = mensaje.Id,
            SenderName = senderName,
            SenderRole = senderRole,
            Body       = mensaje.Body,
            CreatedAt  = mensaje.CreatedAt,
        };

        return Task.FromResult(resultado);
    }

    // ── Gestión (solo admin) ─────────────────────────────────────────────────

    public Task<Response<TInternalRequest>> CambiarEstadoAsync(Guid id, string status)
    {
        var resultado = new Response<TInternalRequest>();

        var allowed = new[] { "nuevo", "en_proceso", "resuelto", "cerrado" };
        if (!allowed.Contains(status))
        {
            resultado.lpError("Estado inválido", $"El estado debe ser: {string.Join(", ", allowed)}.");
            return Task.FromResult(resultado);
        }

        var solicitud = _unitOfWork.InternalRequests.ObtenerEntidad(r => r.Id == id).ReturnValue;
        if (solicitud == null)
        {
            resultado.lpError("No encontrado", "La solicitud no existe.");
            return Task.FromResult(resultado);
        }

        solicitud.Status    = status;
        solicitud.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.InternalRequests.Modificar(solicitud);
        _unitOfWork.Completar();

        var solicitante = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == solicitud.RequestedBy).ReturnValue;
        var admin = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == solicitud.TargetAdminId).ReturnValue;

        resultado.ReturnValue = MapSolicitud(solicitud, solicitante?.DisplayName, admin?.DisplayName);
        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TStaffSimple>>> ListarAdminsAsync()
    {
        var resultado = new Response<IEnumerable<TStaffSimple>>();

        var admins = _unitOfWork.StaffMembers
            .ObtenerEntidades(s => s.Status == "active" && s.Role == "admin")
            .ReturnValue?
            .Select(s => new TStaffSimple { Id = s.Id, DisplayName = s.DisplayName, Role = s.Role })
            ?? Enumerable.Empty<TStaffSimple>();

        resultado.ReturnValue = admins;
        return Task.FromResult(resultado);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private IEnumerable<TInternalRequest> MapConNombres(List<InternalRequest> solicitudes)
    {
        var staffIds = solicitudes
            .SelectMany(r => new[] { r.RequestedBy, r.TargetAdminId })
            .Distinct()
            .ToList();

        var staffDict = staffIds.Count > 0
            ? _unitOfWork.StaffMembers.ObtenerEntidades(s => staffIds.Contains(s.Id)).ReturnValue?
                .ToDictionary(s => s.Id, s => s.DisplayName) ?? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string>();

        return solicitudes.Select(r => MapSolicitud(
            r,
            staffDict.TryGetValue(r.RequestedBy, out var reqName) ? reqName : null,
            staffDict.TryGetValue(r.TargetAdminId, out var adminName) ? adminName : null
        ));
    }

    private static TInternalRequest MapSolicitud(InternalRequest r, string? requestedByName, string? targetAdminName)
    {
        return new TInternalRequest
        {
            Id              = r.Id,
            RequestNumber   = r.RequestNumber,
            Subject         = r.Subject,
            Description     = r.Description,
            Status          = r.Status,
            RequestedBy     = r.RequestedBy,
            RequestedByName = requestedByName,
            TargetAdminId   = r.TargetAdminId,
            TargetAdminName = targetAdminName,
            CreatedAt       = r.CreatedAt,
            UpdatedAt       = r.UpdatedAt,
        };
    }
}
