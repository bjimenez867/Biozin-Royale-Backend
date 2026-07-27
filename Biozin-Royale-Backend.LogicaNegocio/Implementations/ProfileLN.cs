using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class ProfileLN : IProfileLN
{
    private readonly IUnitWork _unitOfWork;
    private readonly IMemoryCache _cache;

    public ProfileLN(IUnitWork unitOfWork, IMemoryCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public Task<Response<TPerfilResultado>> ObtenerPerfilAsync(Guid userId)
    {
        var resultado = new Response<TPerfilResultado>();
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ActualizarPerfilAsync(Guid userId, TActualizarPerfil datos)
    {
        var resultado = new Response<TPerfilResultado>();
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrWhiteSpace(datos.Username) && datos.Username != perfil.Username)
        {
            var enUso = _unitOfWork.Profiles.ObtenerEntidad(p => p.Username == datos.Username).ReturnValue;
            if (enUso is not null)
            {
                resultado.lpError("Usuario en uso", "Ese nombre de usuario ya está ocupado.");
                return Task.FromResult(resultado);
            }
            perfil.Username = datos.Username;
        }

        if (datos.DisplayName is not null) perfil.DisplayName = datos.DisplayName;
        if (datos.Phone is not null) perfil.Phone = datos.Phone;
        if (datos.Country is not null) perfil.Country = datos.Country;
        if (datos.Birthdate is not null) perfil.Birthdate = datos.Birthdate;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CambiarPasswordAsync(Guid userId, string oldPassword, string newPassword)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            resultado.lpError("Contraseña inválida", "La nueva contraseña debe tener al menos 8 caracteres.");
            return Task.FromResult(resultado);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        if (string.IsNullOrEmpty(perfil.Password))
        {
            resultado.lpError("Cuenta sin contraseña", "Tu cuenta inició sesión con Google y no tiene una contraseña configurada.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, perfil.Password))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña actual es incorrecta.");
            return Task.FromResult(resultado);
        }

        perfil.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    private static readonly Regex PinFormato = new(@"^\d{4}$");

    public Task<Response<bool>> CrearPinAsync(Guid userId, string pin)
    {
        var resultado = new Response<bool>();

        if (!PinFormato.IsMatch(pin ?? string.Empty))
        {
            resultado.lpError("PIN inválido", "El PIN debe tener exactamente 4 dígitos.");
            return Task.FromResult(resultado);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("PIN ya configurado", "Ya tienes un PIN configurado. Usa la opción de cambiar PIN.");
            return Task.FromResult(resultado);
        }

        perfil.PinHash = BCrypt.Net.BCrypt.HashPassword(pin);
        perfil.PinEnabled = true;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CambiarPinAsync(Guid userId, string oldPin, string newPin)
    {
        var resultado = new Response<bool>();

        if (!PinFormato.IsMatch(newPin ?? string.Empty))
        {
            resultado.lpError("PIN inválido", "El nuevo PIN debe tener exactamente 4 dígitos.");
            return Task.FromResult(resultado);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        if (string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("Sin PIN configurado", "Todavía no tienes un PIN configurado.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPin, perfil.PinHash))
        {
            resultado.lpError("PIN incorrecto", "El PIN actual es incorrecto.");
            return Task.FromResult(resultado);
        }

        perfil.PinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CambiarEstadoPinAsync(Guid userId, string pin, bool enabled)
    {
        var resultado = new Response<bool>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        if (string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("Sin PIN configurado", "Todavía no tienes un PIN configurado.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(pin, perfil.PinHash))
        {
            resultado.lpError("PIN incorrecto", "El PIN ingresado es incorrecto.");
            return Task.FromResult(resultado);
        }

        perfil.PinEnabled = enabled;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CambiarEstadoTwoFactorAsync(Guid userId, string password, bool enabled)
    {
        var resultado = new Response<bool>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        // Las cuentas de Google no tienen contraseña propia, así que no hay nada que
        // verificar contra ellas: se permite el cambio directo. Las cuentas con
        // contraseña sí deben confirmarla, igual que para PIN.
        if (!string.IsNullOrEmpty(perfil.Password) && !BCrypt.Net.BCrypt.Verify(password, perfil.Password))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña ingresada es incorrecta.");
            return Task.FromResult(resultado);
        }

        perfil.TwoFactorEnabled = enabled;
        perfil.TwoFactorCode = null;
        perfil.TwoFactorCodeExpiresAt = null;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<List<TSession>>> ObtenerSesionesAsync(Guid userId, Guid? currentSessionId)
    {
        var resultado = new Response<List<TSession>>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        var sesiones = _unitOfWork.Sessions
            .ObtenerEntidades(s => s.ProfileId == perfil.Id && s.IsActive).ReturnValue
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new TSession
            {
                Id = s.Id,
                DeviceLabel = s.DeviceLabel,
                IpAddress = s.IpAddress,
                CreatedAt = s.CreatedAt,
                IsCurrent = currentSessionId.HasValue && s.Id == currentSessionId.Value,
            })
            .ToList();

        resultado.ReturnValue = sesiones;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CerrarSesionAsync(Guid userId, Guid sessionId)
    {
        var resultado = new Response<bool>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        var sesion = _unitOfWork.Sessions.ObtenerEntidad(s => s.Id == sessionId && s.ProfileId == perfil.Id).ReturnValue;
        if (sesion is null || !sesion.IsActive)
        {
            resultado.lpError("Sesión no encontrada", "Esa sesión ya no está activa.");
            return Task.FromResult(resultado);
        }

        sesion.IsActive = false;
        sesion.RevokedAt = DateTime.UtcNow;
        _unitOfWork.Sessions.Modificar(sesion);
        _unitOfWork.Completar();

        _cache.Set(sesion.Id, true, TimeSpan.FromSeconds(30));

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CerrarOtrasSesionesAsync(Guid userId, Guid currentSessionId)
    {
        var resultado = new Response<bool>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        var otras = _unitOfWork.Sessions
            .ObtenerEntidades(s => s.ProfileId == perfil.Id && s.IsActive && s.Id != currentSessionId).ReturnValue
            .ToList();

        foreach (var sesion in otras)
        {
            sesion.IsActive = false;
            sesion.RevokedAt = DateTime.UtcNow;
            _unitOfWork.Sessions.Modificar(sesion);
            _cache.Set(sesion.Id, true, TimeSpan.FromSeconds(30));
        }

        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<List<TAdminUser>>> ObtenerUsuariosAsync(Guid adminId)
    {
        var resultado = new Response<List<TAdminUser>>();

        var staffEmail = _unitOfWork.StaffMembers
            .ObtenerEntidad(s => s.Id == adminId).ReturnValue?.Email;
        var esAdmin = staffEmail is not null && CredentialsGenerator.DetectRole(staffEmail) == "admin";

        if (!esAdmin)
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var perfiles = _unitOfWork.Profiles
            .ObtenerEntidades(p => !p.IsGuest)
            .ReturnValue ?? [];

        resultado.ReturnValue = perfiles
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new TAdminUser
            {
                Id = p.UserId,
                Username = p.Username,
                DisplayName = p.DisplayName,
                Email = p.Email,
                Status = p.Status,
                CreatedAt = p.CreatedAt
            }).ToList();

        return Task.FromResult(resultado);
    }

    public Task<Response<TUserBlockInfo>> ObtenerBloqueoActivoAsync(Guid adminId, Guid userId)
    {
        var resultado = new Response<TUserBlockInfo>();

        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Usuario no encontrado", "No existe un perfil con ese identificador.");
            return Task.FromResult(resultado);
        }

        var bloqueo = _unitOfWork.UserBlocks
            .ObtenerEntidad(b => b.ProfileId == perfil.Id && b.IsActive).ReturnValue;

        if (bloqueo is not null)
        {
            var bloqueadoPor = _unitOfWork.StaffMembers
                .ObtenerEntidad(s => s.Id == bloqueo.BlockedBy).ReturnValue;

            resultado.ReturnValue = new TUserBlockInfo
            {
                Id = bloqueo.Id,
                Reason = bloqueo.Reason,
                Message = bloqueo.Message,
                BlockedAt = bloqueo.BlockedAt,
                BlockedByName = bloqueadoPor?.DisplayName ?? bloqueadoPor?.Username ?? "Admin",
            };
        }

        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> BloquearUsuarioAsync(Guid adminId, Guid userId, TBlockUserRequest datos)
    {
        var resultado = new Response<bool>();

        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var razonesValidas = new[] { "fraude", "incumplimiento", "conducta", "sospechoso", "otro" };
        if (!razonesValidas.Contains(datos.Reason))
        {
            resultado.lpError("Datos inválidos", "La razón de bloqueo no es válida.");
            return Task.FromResult(resultado);
        }

        if (string.IsNullOrWhiteSpace(datos.Message))
        {
            resultado.lpError("Datos inválidos", "El mensaje para el usuario es requerido.");
            return Task.FromResult(resultado);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Usuario no encontrado", "No existe un perfil con ese identificador.");
            return Task.FromResult(resultado);
        }

        if (perfil.Status == "blocked")
        {
            resultado.lpError("Ya bloqueado", "Este usuario ya se encuentra bloqueado.");
            return Task.FromResult(resultado);
        }

        var bloqueo = new UserBlock
        {
            Id = Guid.NewGuid(),
            ProfileId = perfil.Id,
            BlockedBy = adminId,
            Reason = datos.Reason,
            Message = datos.Message,
            BlockedAt = DateTime.UtcNow,
            IsActive = true,
        };

        perfil.Status = "blocked";
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.UserBlocks.Insertar(bloqueo);
        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> DesbloquearUsuarioAsync(Guid adminId, Guid userId)
    {
        var resultado = new Response<bool>();

        if (!EsAdmin(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return Task.FromResult(resultado);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Usuario no encontrado", "No existe un perfil con ese identificador.");
            return Task.FromResult(resultado);
        }

        var bloqueo = _unitOfWork.UserBlocks
            .ObtenerEntidad(b => b.ProfileId == perfil.Id && b.IsActive).ReturnValue;

        if (bloqueo is null)
        {
            resultado.lpError("No bloqueado", "Este usuario no tiene un bloqueo activo.");
            return Task.FromResult(resultado);
        }

        bloqueo.IsActive = false;
        bloqueo.UnblockedAt = DateTime.UtcNow;
        bloqueo.UnblockedBy = adminId;

        perfil.Status = "active";
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.UserBlocks.Modificar(bloqueo);
        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    private bool EsAdmin(Guid adminId)
    {
        var staffEmail = _unitOfWork.StaffMembers
            .ObtenerEntidad(s => s.Id == adminId).ReturnValue?.Email;
        return staffEmail is not null && CredentialsGenerator.DetectRole(staffEmail) == "admin";
    }

    public Task<Response<TEstadisticas>> ObtenerEstadisticasAsync(Guid userId)
    {
        var resultado = new Response<TEstadisticas>();
        var stats = _unitOfWork.Statistics.ObtenerEntidad(s => s.UserId == userId).ReturnValue;

        // Sin filas en bets para este usuario (aún no jugó): se devuelven ceros, no error.
        resultado.ReturnValue = stats is null
            ? new TEstadisticas()
            : new TEstadisticas
            {
                PartidasJugadas = stats.PartidasJugadas,
                PartidasGanadas = stats.PartidasGanadas,
                ApostadoTotal = stats.ApostadoTotal,
                GananciasNetas = stats.GananciasNetas
            };
        return Task.FromResult(resultado);
    }
}
