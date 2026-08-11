using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class StaffLN : IStaffLN
{
    private static readonly string[] RolesValidos = { "admin", "soporte" };
    private const int MaxIntentosFallidos = 5;
    private const int MinutosBloqueo = 15;

    private readonly IUnitWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;

    public StaffLN(IUnitWork unitOfWork, IConfiguration configuration, IEmailService emailService, IMemoryCache cache)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _emailService = emailService;
        _cache = cache;
    }

    public async Task<Response<TPerfilResultado>> CrearMiembroAsync(TCrearStaffMember datos, Guid creadoPorId)
    {
        var resultado = new Response<TPerfilResultado>();

        var rol = datos.Role.Trim().ToLowerInvariant();
        if (!RolesValidos.Contains(rol))
        {
            resultado.lpError("Rol inválido", "El rol debe ser 'admin' o 'soporte'.");
            return resultado;
        }

        if (string.IsNullOrWhiteSpace(datos.Nombre) || string.IsNullOrWhiteSpace(datos.CorreoContacto))
        {
            resultado.lpError("Datos inválidos", "El nombre y el correo de contacto son obligatorios.");
            return resultado;
        }

        var baseEmail = CredentialsGenerator.GenerateBaseEmailWithFullName(datos.Nombre);
        var email = await GenerarEmailUnicoAsync(baseEmail, rol);
        var username = await GenerarUsernameUnicoAsync(datos.Nombre);
        var passwordTemporal = CredentialsGenerator.GeneratePassword();
        var ahora = DateTime.UtcNow;

        var staff = new StaffMember
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = datos.Nombre,
            Email = email,
            Phone = datos.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordTemporal),
            Status = "active",
            MustChangePassword = true,
            CreatedBy = creadoPorId,
            CreatedAt = ahora,
            UpdatedAt = ahora
        };

        _unitOfWork.StaffMembers.Insertar(staff);
        await _unitOfWork.CompletarAsync();

        try
        {
            await _emailService.EnviarCredencialesStaffAsync(
                correoDestino: datos.CorreoContacto,
                nombre: datos.Nombre,
                correoEmpresarial: email,
                password: passwordTemporal,
                rol: rol == "admin" ? "Administrador" : "Soporte",
                correoRemitente: _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com");
        }
        catch (Exception)
        {
            // El miembro ya quedó creado; si Mailtrap falla, la pantalla de "credenciales
            // creadas" del frontend sigue mostrando la contraseña en claro como respaldo.
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null, tempPassword: passwordTemporal);
        return resultado;
    }

    public async Task<Response<IEnumerable<TPerfilResultado>>> ListarMiembrosAsync()
    {
        var resultado = new Response<IEnumerable<TPerfilResultado>>();

        var miembros = await _unitOfWork.StaffMembers.ListarAsync();

        resultado.ReturnValue = miembros
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => StaffMapper.MapearComoPerfil(s, token: null));
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> LoginAsync(string email, string password, string? userAgent, string? ipAddress)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Email == email);
        var credencialesValidas = staff is not null && BCrypt.Net.BCrypt.Verify(password, staff.PasswordHash);

        if (!credencialesValidas)
        {
            // Si ya está bloqueado no se sigue contando: el bloqueo tiene duración fija.
            if (staff is not null && !EstaBloqueado(staff, out _))
            {
                await RegistrarIntentoFallidoAsync(staff);
            }
            resultado.lpError("Credenciales inválidas", "El correo o la contraseña son incorrectos.");
            return resultado;
        }

        if (EstaBloqueado(staff!, out var mensajeBloqueo))
        {
            resultado.lpError("Cuenta bloqueada", mensajeBloqueo);
            return resultado;
        }

        await RestablecerIntentosFallidosAsync(staff!);

        var (staffToken, staffRefresh) = await GenerarTokenConSesionAsync(staff!, userAgent, ipAddress);
        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff!, staffToken, staffRefresh);
        return resultado;
    }

    // Mismo mecanismo que AuthLN, duplicado aquí porque Profile y StaffMember no
    // comparten un tipo base (StaffLN tampoco comparte otras utilidades con AuthLN,
    // ej. RegistrarEvento ya está duplicado abajo).
    private static bool EstaBloqueado(StaffMember staff, out string mensaje)
    {
        if (staff.LockedUntil is not null && staff.LockedUntil > DateTime.UtcNow)
        {
            var minutos = Math.Max(1, (int)Math.Ceiling((staff.LockedUntil.Value - DateTime.UtcNow).TotalMinutes));
            mensaje = $"Demasiados intentos fallidos. Intenta de nuevo en {minutos} minuto(s).";
            return true;
        }
        mensaje = string.Empty;
        return false;
    }

    private async Task RegistrarIntentoFallidoAsync(StaffMember staff)
    {
        staff.FailedLoginAttempts++;
        if (staff.FailedLoginAttempts >= MaxIntentosFallidos)
        {
            staff.LockedUntil = DateTime.UtcNow.AddMinutes(MinutosBloqueo);
            staff.FailedLoginAttempts = 0;
            RegistrarEvento(staff.Id, "account_locked");
        }
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        await _unitOfWork.CompletarAsync();
    }

    private async Task RestablecerIntentosFallidosAsync(StaffMember staff)
    {
        if (staff.FailedLoginAttempts == 0 && staff.LockedUntil is null) return;

        staff.FailedLoginAttempts = 0;
        staff.LockedUntil = null;
        _unitOfWork.StaffMembers.Modificar(staff);
        await _unitOfWork.CompletarAsync();
    }

    // Mismo patrón que AuthLN.GenerarTokenConSesion: sessionId estable (Session.Id),
    // jti por-token separado; devuelve (accessToken, refreshToken).
    private async Task<(string token, string refreshToken)> GenerarTokenConSesionAsync(StaffMember staff, string? userAgent, string? ipAddress)
    {
        var sessionId = Guid.NewGuid();
        var jti = Guid.NewGuid();
        var token = JwtTokenFactory.GenerarToken(_configuration, staff.Id, staff.Email, CredentialsGenerator.DetectRole(staff.Email), jti, sessionId);

        var refreshRaw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshRaw)));

        _unitOfWork.Sessions.Insertar(new Session
        {
            Id = sessionId,
            StaffId = staff.Id,
            DeviceLabel = DeviceParser.AnalizarUserAgent(userAgent),
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            RefreshTokenHash = refreshHash,
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        RegistrarEvento(staff.Id, "login");
        await _unitOfWork.CompletarAsync();

        return (token, refreshRaw);
    }

    // No hace su propio Completar(): se inserta junto con el resto de cambios del
    // método que la llama (mismo patrón que ProfileLN/AuthLN.RegistrarEvento).
    private void RegistrarEvento(Guid staffId, string eventType)
    {
        _unitOfWork.SecurityEvents.Insertar(new SecurityEvent
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            EventType = eventType,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task<Response<List<TSecurityEvent>>> ObtenerHistorialSeguridadAsync(Guid staffId)
    {
        var resultado = new Response<List<TSecurityEvent>>();

        var eventos = await _unitOfWork.SecurityEvents.ObtenerEntidadesAsync(e => e.StaffId == staffId);

        resultado.ReturnValue = eventos
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .Select(e => new TSecurityEvent { EventType = e.EventType, CreatedAt = e.CreatedAt })
            .ToList();

        return resultado;
    }

    public async Task<Response<List<TSession>>> ObtenerSesionesAsync(Guid staffId, Guid? currentSessionId)
    {
        var resultado = new Response<List<TSession>>();

        var sesiones = await _unitOfWork.Sessions.ObtenerEntidadesAsync(s => s.StaffId == staffId && s.IsActive);

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

    public async Task<Response<bool>> CerrarSesionAsync(Guid staffId, Guid sessionId)
    {
        var resultado = new Response<bool>();

        var sesion = await _unitOfWork.Sessions.ObtenerEntidadAsync(s => s.Id == sessionId && s.StaffId == staffId);
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

    public async Task<Response<bool>> CerrarOtrasSesionesAsync(Guid staffId, Guid currentSessionId)
    {
        var resultado = new Response<bool>();

        var otras = await _unitOfWork.Sessions
            .ObtenerEntidadesAsync(s => s.StaffId == staffId && s.IsActive && s.Id != currentSessionId);

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

    public async Task<Response<TPerfilResultado>> ObtenerMeAsync(Guid staffId)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == staffId);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> ActualizarMeAsync(Guid staffId, TActualizarStaffMember datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == staffId);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        if (!string.IsNullOrWhiteSpace(datos.DisplayName))
            staff.DisplayName = datos.DisplayName.Trim();

        staff.Phone = string.IsNullOrWhiteSpace(datos.Phone) ? null : datos.Phone.Trim();
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> ObtenerMiembroAsync(Guid id)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == id);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        string? createdByName = null;
        if (staff.CreatedBy.HasValue)
        {
            var creator = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == staff.CreatedBy.Value);
            createdByName = creator?.DisplayName ?? creator?.Username;
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null, createdByName: createdByName);
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> ActualizarMiembroAsync(Guid id, TActualizarStaffMember datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == id);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        if (!string.IsNullOrWhiteSpace(datos.DisplayName))
            staff.DisplayName = datos.DisplayName.Trim();

        staff.Phone = string.IsNullOrWhiteSpace(datos.Phone) ? null : datos.Phone.Trim();
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> CambiarStatusMiembroAsync(Guid id, string nuevoStatus)
    {
        var resultado = new Response<TPerfilResultado>();

        var statusNorm = nuevoStatus.Trim().ToLowerInvariant();
        if (statusNorm != "active" && statusNorm != "inactive")
        {
            resultado.lpError("Estado inválido", "El estado debe ser 'active' o 'inactive'.");
            return resultado;
        }

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == id);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        staff.Status = statusNorm;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return resultado;
    }

    public async Task<Response<bool>> EliminarMiembroAsync(Guid id)
    {
        var resultado = new Response<bool>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == id);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        _unitOfWork.StaffMembers.Eliminar(staff);
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> CambiarPasswordAsync(Guid staffId, string oldPassword, string newPassword)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            resultado.lpError("Contraseña inválida", "La nueva contraseña debe tener al menos 8 caracteres.");
            return resultado;
        }

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == staffId);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, staff.PasswordHash))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña actual es incorrecta.");
            return resultado;
        }

        staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        staff.MustChangePassword = false;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        RegistrarEvento(staff.Id, "password_change");
        await _unitOfWork.CompletarAsync();

        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<bool>> RestablecerPasswordStaffAsync(Guid id)
    {
        var resultado = new Response<bool>();

        var staff = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Id == id);
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        var nuevaPassword = CredentialsGenerator.GeneratePassword();
        staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(nuevaPassword);
        staff.MustChangePassword = true;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        await _unitOfWork.CompletarAsync();

        var rol = CredentialsGenerator.DetectRole(staff.Email) == "admin" ? "Administrador" : "Soporte";
        var remitente = _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com";

        try
        {
            await _emailService.EnviarCredencialesStaffAsync(
                correoDestino: staff.Email,
                nombre: staff.DisplayName,
                correoEmpresarial: staff.Email,
                password: nuevaPassword,
                rol: rol,
                correoRemitente: remitente);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RestablecerPasswordStaff] Error enviando correo a {staff.Email}: {ex.GetType().Name} — {ex.Message}");
        }

        resultado.ReturnValue = true;
        return resultado;
    }

    private async Task<string> GenerarEmailUnicoAsync(string baseEmail, string rol)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = rol == "admin"
                ? CredentialsGenerator.BuildEmailAdmin(baseEmail, sufijo)
                : CredentialsGenerator.BuildSupportEmail(baseEmail, sufijo);

            var enUso = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Email == candidato);
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }

    private async Task<string> GenerarUsernameUnicoAsync(string nombreBase)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = CredentialsGenerator.GenerateUsername(nombreBase, sufijo);
            var enUso = await _unitOfWork.StaffMembers.ObtenerEntidadAsync(s => s.Username == candidato);
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }
}
