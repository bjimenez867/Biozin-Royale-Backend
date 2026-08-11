using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
    private readonly string _avatarsBaseUrl;

    public ProfileLN(IUnitWork unitOfWork, IMemoryCache cache, IConfiguration config)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _avatarsBaseUrl = config["Supabase:AvatarsBucketBaseUrl"] ?? "";
    }

    // No hace su propio Completar(): se inserta junto con el resto de cambios del
    // método que la llama, para que quede en el mismo SaveChanges (mismo patrón que
    // Sessions en AuthLN.GenerarTokenConSesion).
    private void RegistrarEvento(Guid profileId, string eventType)
    {
        _unitOfWork.SecurityEvents.Insertar(new SecurityEvent
        {
            Id = Guid.NewGuid(),
            ProfileId = profileId,
            EventType = eventType,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task<Response<TPerfilResultado>> ObtenerPerfilAsync(Guid userId)
    {
        var resultado = new Response<TPerfilResultado>();
        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null, avatarUrl: await ResolverAvatarUrlAsync(perfil));
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> ActualizarPerfilAsync(Guid userId, TActualizarPerfil datos)
    {
        var resultado = new Response<TPerfilResultado>();
        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        if (!string.IsNullOrWhiteSpace(datos.Username) && datos.Username != perfil.Username)
        {
            var enUso = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.Username == datos.Username);
            if (enUso is not null)
            {
                resultado.lpError("Usuario en uso", "Ese nombre de usuario ya está ocupado.");
                return resultado;
            }
            perfil.Username = datos.Username;
        }

        if (datos.DisplayName is not null) perfil.DisplayName = datos.DisplayName;
        if (datos.Phone is not null) perfil.Phone = datos.Phone;
        if (datos.Country is not null) perfil.Country = datos.Country;
        if (datos.Birthdate is not null) perfil.Birthdate = datos.Birthdate;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null, avatarUrl: await ResolverAvatarUrlAsync(perfil));
        return resultado;
    }

    public async Task<Response<bool>> CambiarPasswordAsync(Guid userId, string oldPassword, string newPassword)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            resultado.lpError("Contraseña inválida", "La nueva contraseña debe tener al menos 8 caracteres.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        if (string.IsNullOrEmpty(perfil.Password))
        {
            resultado.lpError("Cuenta sin contraseña", "Tu cuenta inició sesión con Google y no tiene una contraseña configurada.");
            return resultado;
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, perfil.Password))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña actual es incorrecta.");
            return resultado;
        }

        perfil.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        RegistrarEvento(perfil.Id, "password_change");
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    private static readonly Regex PinFormato = new(@"^\d{4}$");

    public async Task<Response<bool>> CrearPinAsync(Guid userId, string pin)
    {
        var resultado = new Response<bool>();

        if (!PinFormato.IsMatch(pin ?? string.Empty))
        {
            resultado.lpError("PIN inválido", "El PIN debe tener exactamente 4 dígitos.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        if (!string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("PIN ya configurado", "Ya tienes un PIN configurado. Usa la opción de cambiar PIN.");
            return resultado;
        }

        perfil.PinHash = BCrypt.Net.BCrypt.HashPassword(pin);
        perfil.PinEnabled = true;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        RegistrarEvento(perfil.Id, "pin_created");
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> CambiarPinAsync(Guid userId, string oldPin, string newPin)
    {
        var resultado = new Response<bool>();

        if (!PinFormato.IsMatch(newPin ?? string.Empty))
        {
            resultado.lpError("PIN inválido", "El nuevo PIN debe tener exactamente 4 dígitos.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        if (string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("Sin PIN configurado", "Todavía no tienes un PIN configurado.");
            return resultado;
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPin, perfil.PinHash))
        {
            resultado.lpError("PIN incorrecto", "El PIN actual es incorrecto.");
            return resultado;
        }

        perfil.PinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        RegistrarEvento(perfil.Id, "pin_changed");
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> VerificarPinAsync(Guid userId, string pin)
    {
        var resultado = new Response<bool>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        if (!perfil.PinEnabled || string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("Sin PIN configurado", "Todavía no tienes un PIN activo.");
            return resultado;
        }

        if (!BCrypt.Net.BCrypt.Verify(pin, perfil.PinHash))
        {
            resultado.lpError("PIN incorrecto", "El PIN ingresado es incorrecto.");
            return resultado;
        }

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> CambiarEstadoPinAsync(Guid userId, string pin, bool enabled)
    {
        var resultado = new Response<bool>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        if (string.IsNullOrEmpty(perfil.PinHash))
        {
            resultado.lpError("Sin PIN configurado", "Todavía no tienes un PIN configurado.");
            return resultado;
        }

        if (!BCrypt.Net.BCrypt.Verify(pin, perfil.PinHash))
        {
            resultado.lpError("PIN incorrecto", "El PIN ingresado es incorrecto.");
            return resultado;
        }

        perfil.PinEnabled = enabled;
        if (!enabled)
        {
            // Al desactivar se elimina el PIN almacenado: para volver a activarlo el
            // usuario debe crear un PIN nuevo (CrearPinAsync), no reutilizar el anterior.
            perfil.PinHash = null;
        }
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        RegistrarEvento(perfil.Id, enabled ? "pin_enabled" : "pin_disabled");
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> CambiarEstadoTwoFactorAsync(Guid userId, string password, bool enabled)
    {
        var resultado = new Response<bool>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        // Las cuentas de Google no tienen contraseña propia, así que no hay nada que
        // verificar contra ellas: se permite el cambio directo. Las cuentas con
        // contraseña sí deben confirmarla, igual que para PIN.
        if (!string.IsNullOrEmpty(perfil.Password) && !BCrypt.Net.BCrypt.Verify(password, perfil.Password))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña ingresada es incorrecta.");
            return resultado;
        }

        perfil.TwoFactorEnabled = enabled;
        perfil.TwoFactorCode = null;
        perfil.TwoFactorCodeExpiresAt = null;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        RegistrarEvento(perfil.Id, enabled ? "twofactor_enabled" : "twofactor_disabled");
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<List<TSecurityEvent>>> ObtenerHistorialSeguridadAsync(Guid userId)
    {
        var resultado = new Response<List<TSecurityEvent>>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        // 50: sirve tanto a la tarjeta compacta de Ajustes (solo muestra los primeros 3)
        // como a la pantalla de "Historial de seguridad" completo.
        var eventos = await _unitOfWork.SecurityEvents.ObtenerEntidadesAsync(e => e.ProfileId == perfil.Id);

        resultado.ReturnValue = eventos
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .Select(e => new TSecurityEvent { EventType = e.EventType, CreatedAt = e.CreatedAt })
            .ToList();

        return resultado;
    }

    public async Task<Response<List<TSession>>> ObtenerSesionesAsync(Guid userId, Guid? currentSessionId)
    {
        var resultado = new Response<List<TSession>>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        var sesiones = await _unitOfWork.Sessions.ObtenerEntidadesAsync(s => s.ProfileId == perfil.Id && s.IsActive);

        resultado.ReturnValue = sesiones
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

        return resultado;
    }

    public async Task<Response<bool>> CerrarSesionAsync(Guid userId, Guid sessionId)
    {
        var resultado = new Response<bool>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        var sesion = await _unitOfWork.Sessions.ObtenerEntidadAsync(s => s.Id == sessionId && s.ProfileId == perfil.Id);
        if (sesion is null || !sesion.IsActive)
        {
            resultado.lpError("Sesión no encontrada", "Esa sesión ya no está activa.");
            return resultado;
        }

        sesion.IsActive = false;
        sesion.RevokedAt = DateTime.UtcNow;
        _unitOfWork.Sessions.Modificar(sesion);
        await _unitOfWork.CompletarAsync();

        _cache.Set(sesion.Id, true, TimeSpan.FromSeconds(30));

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> CerrarOtrasSesionesAsync(Guid userId, Guid currentSessionId)
    {
        var resultado = new Response<bool>();

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return resultado;
        }

        var otras = await _unitOfWork.Sessions
            .ObtenerEntidadesAsync(s => s.ProfileId == perfil.Id && s.IsActive && s.Id != currentSessionId);

        foreach (var sesion in otras)
        {
            sesion.IsActive = false;
            sesion.RevokedAt = DateTime.UtcNow;
            _unitOfWork.Sessions.Modificar(sesion);
            _cache.Set(sesion.Id, true, TimeSpan.FromSeconds(30));
        }

        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<List<TAdminUser>>> ObtenerUsuariosAsync(Guid adminId)
    {
        var resultado = new Response<List<TAdminUser>>();

        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var perfiles = await _unitOfWork.Profiles.ObtenerEntidadesAsync(p => !p.IsGuest);

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

        return resultado;
    }

    public async Task<Response<TUserBlockInfo>> ObtenerBloqueoActivoAsync(Guid adminId, Guid userId)
    {
        var resultado = new Response<TUserBlockInfo>();

        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Usuario no encontrado", "No existe un perfil con ese identificador.");
            return resultado;
        }

        var bloqueo = await _unitOfWork.UserBlocks
            .ObtenerEntidadAsync(b => b.ProfileId == perfil.Id && b.IsActive);

        if (bloqueo is not null)
        {
            var bloqueadoPor = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == bloqueo.BlockedBy);

            resultado.ReturnValue = new TUserBlockInfo
            {
                Id = bloqueo.Id,
                Reason = bloqueo.Reason,
                Message = bloqueo.Message,
                BlockedAt = bloqueo.BlockedAt,
                BlockedByName = bloqueadoPor?.DisplayName ?? bloqueadoPor?.Username ?? "Admin",
            };
        }

        return resultado;
    }

    public async Task<Response<bool>> BloquearUsuarioAsync(Guid adminId, Guid userId, TBlockUserRequest datos)
    {
        var resultado = new Response<bool>();

        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var razonesValidas = new[] { "fraude", "incumplimiento", "conducta", "sospechoso", "otro" };
        if (!razonesValidas.Contains(datos.Reason))
        {
            resultado.lpError("Datos inválidos", "La razón de bloqueo no es válida.");
            return resultado;
        }

        if (string.IsNullOrWhiteSpace(datos.Message))
        {
            resultado.lpError("Datos inválidos", "El mensaje para el usuario es requerido.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Usuario no encontrado", "No existe un perfil con ese identificador.");
            return resultado;
        }

        if (perfil.Status == "blocked")
        {
            resultado.lpError("Ya bloqueado", "Este usuario ya se encuentra bloqueado.");
            return resultado;
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
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> DesbloquearUsuarioAsync(Guid adminId, Guid userId)
    {
        var resultado = new Response<bool>();

        if (!await EsAdminAsync(adminId))
        {
            resultado.lpError("Acceso denegado", "No tienes permisos para esta acción.");
            return resultado;
        }

        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null)
        {
            resultado.lpError("Usuario no encontrado", "No existe un perfil con ese identificador.");
            return resultado;
        }

        var bloqueo = await _unitOfWork.UserBlocks
            .ObtenerEntidadAsync(b => b.ProfileId == perfil.Id && b.IsActive);

        if (bloqueo is null)
        {
            resultado.lpError("No bloqueado", "Este usuario no tiene un bloqueo activo.");
            return resultado;
        }

        bloqueo.IsActive = false;
        bloqueo.UnblockedAt = DateTime.UtcNow;
        bloqueo.UnblockedBy = adminId;

        perfil.Status = "active";
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.UserBlocks.Modificar(bloqueo);
        _unitOfWork.Profiles.Modificar(perfil);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    private async Task<bool> EsAdminAsync(Guid adminId)
    {
        var staffEmail = (await _unitOfWork.StaffMembers
            .ObtenerEntidadAsync(s => s.Id == adminId))?.Email;
        return staffEmail is not null && CredentialsGenerator.DetectRole(staffEmail) == "admin";
    }

    public async Task<Response<bool>> CheckUsernameAsync(string username, Guid userId)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3 || username.Length > 20)
        {
            resultado.ReturnValue = false;
            return resultado;
        }

        // El propio username actual del usuario siempre se considera disponible
        var perfil = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil?.Username == username)
        {
            resultado.ReturnValue = true;
            return resultado;
        }

        var enUso = await _unitOfWork.Profiles.ObtenerEntidadAsync(p => p.Username == username);
        resultado.ReturnValue = enUso is null;
        return resultado;
    }

    public async Task<Response<TEstadisticas>> ObtenerEstadisticasAsync(Guid userId)
    {
        var resultado = new Response<TEstadisticas>();
        var stats = await _unitOfWork.Statistics.ObtenerEntidadAsync(s => s.UserId == userId);

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
        return resultado;
    }

    private async Task<string?> ResolverAvatarUrlAsync(Profile perfil)
    {
        if (perfil.AvatarId is null) return null;
        var avatar = await _unitOfWork.Avatars.ObtenerEntidadAsync(a => a.Id == perfil.AvatarId);
        return avatar is null ? null : $"{_avatarsBaseUrl}/{avatar.StoragePath}";
    }
}
