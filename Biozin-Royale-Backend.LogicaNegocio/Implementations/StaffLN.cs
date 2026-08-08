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
        var email = GenerarEmailUnico(baseEmail, rol);
        var username = GenerarUsernameUnico(datos.Nombre);
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
        _unitOfWork.Completar();

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

    public Task<Response<IEnumerable<TPerfilResultado>>> ListarMiembrosAsync()
    {
        var resultado = new Response<IEnumerable<TPerfilResultado>>();

        var miembros = _unitOfWork.StaffMembers.Listar().ReturnValue!
            .OrderByDescending(s => s.CreatedAt);

        resultado.ReturnValue = miembros.Select(s => StaffMapper.MapearComoPerfil(s, token: null));
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> LoginAsync(string email, string password, string? userAgent, string? ipAddress)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Email == email).ReturnValue;
        var credencialesValidas = staff is not null && BCrypt.Net.BCrypt.Verify(password, staff.PasswordHash);

        if (!credencialesValidas)
        {
            // Si ya está bloqueado no se sigue contando: el bloqueo tiene duración fija.
            if (staff is not null && !EstaBloqueado(staff, out _))
            {
                RegistrarIntentoFallido(staff);
            }
            resultado.lpError("Credenciales inválidas", "El correo o la contraseña son incorrectos.");
            return Task.FromResult(resultado);
        }

        if (EstaBloqueado(staff!, out var mensajeBloqueo))
        {
            resultado.lpError("Cuenta bloqueada", mensajeBloqueo);
            return Task.FromResult(resultado);
        }

        RestablecerIntentosFallidos(staff!);

        var (staffToken, staffRefresh) = GenerarTokenConSesion(staff!, userAgent, ipAddress);
        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff!, staffToken, staffRefresh);
        return Task.FromResult(resultado);
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

    private void RegistrarIntentoFallido(StaffMember staff)
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
        _unitOfWork.Completar();
    }

    private void RestablecerIntentosFallidos(StaffMember staff)
    {
        if (staff.FailedLoginAttempts == 0 && staff.LockedUntil is null) return;

        staff.FailedLoginAttempts = 0;
        staff.LockedUntil = null;
        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();
    }

    // Mismo patrón que AuthLN.GenerarTokenConSesion: sessionId estable (Session.Id),
    // jti por-token separado; devuelve (accessToken, refreshToken).
    private (string token, string refreshToken) GenerarTokenConSesion(StaffMember staff, string? userAgent, string? ipAddress)
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
        _unitOfWork.Completar();

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

    public Task<Response<List<TSecurityEvent>>> ObtenerHistorialSeguridadAsync(Guid staffId)
    {
        var resultado = new Response<List<TSecurityEvent>>();

        var eventos = _unitOfWork.SecurityEvents
            .ObtenerEntidades(e => e.StaffId == staffId).ReturnValue
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .Select(e => new TSecurityEvent { EventType = e.EventType, CreatedAt = e.CreatedAt })
            .ToList();

        resultado.ReturnValue = eventos;
        return Task.FromResult(resultado);
    }

    public Task<Response<List<TSession>>> ObtenerSesionesAsync(Guid staffId, Guid? currentSessionId)
    {
        var resultado = new Response<List<TSession>>();

        var sesiones = _unitOfWork.Sessions
            .ObtenerEntidades(s => s.StaffId == staffId && s.IsActive).ReturnValue
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

    public Task<Response<bool>> CerrarSesionAsync(Guid staffId, Guid sessionId)
    {
        var resultado = new Response<bool>();

        var sesion = _unitOfWork.Sessions.ObtenerEntidad(s => s.Id == sessionId && s.StaffId == staffId).ReturnValue;
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

    public Task<Response<bool>> CerrarOtrasSesionesAsync(Guid staffId, Guid currentSessionId)
    {
        var resultado = new Response<bool>();

        var otras = _unitOfWork.Sessions
            .ObtenerEntidades(s => s.StaffId == staffId && s.IsActive && s.Id != currentSessionId).ReturnValue
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

    public Task<Response<TPerfilResultado>> ObtenerMeAsync(Guid staffId)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffId).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ActualizarMeAsync(Guid staffId, TActualizarStaffMember datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffId).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrWhiteSpace(datos.DisplayName))
            staff.DisplayName = datos.DisplayName.Trim();

        staff.Phone = string.IsNullOrWhiteSpace(datos.Phone) ? null : datos.Phone.Trim();
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ObtenerMiembroAsync(Guid id)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        string? createdByName = null;
        if (staff.CreatedBy.HasValue)
        {
            var creator = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staff.CreatedBy.Value).ReturnValue;
            createdByName = creator?.DisplayName ?? creator?.Username;
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null, createdByName: createdByName);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ActualizarMiembroAsync(Guid id, TActualizarStaffMember datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrWhiteSpace(datos.DisplayName))
            staff.DisplayName = datos.DisplayName.Trim();

        staff.Phone = string.IsNullOrWhiteSpace(datos.Phone) ? null : datos.Phone.Trim();
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> CambiarStatusMiembroAsync(Guid id, string nuevoStatus)
    {
        var resultado = new Response<TPerfilResultado>();

        var statusNorm = nuevoStatus.Trim().ToLowerInvariant();
        if (statusNorm != "active" && statusNorm != "inactive")
        {
            resultado.lpError("Estado inválido", "El estado debe ser 'active' o 'inactive'.");
            return Task.FromResult(resultado);
        }

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        staff.Status = statusNorm;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> EliminarMiembroAsync(Guid id)
    {
        var resultado = new Response<bool>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        _unitOfWork.StaffMembers.Eliminar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CambiarPasswordAsync(Guid staffId, string oldPassword, string newPassword)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            resultado.lpError("Contraseña inválida", "La nueva contraseña debe tener al menos 8 caracteres.");
            return Task.FromResult(resultado);
        }

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffId).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, staff.PasswordHash))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña actual es incorrecta.");
            return Task.FromResult(resultado);
        }

        staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        staff.MustChangePassword = false;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        RegistrarEvento(staff.Id, "password_change");
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public async Task<Response<bool>> RestablecerPasswordStaffAsync(Guid id)
    {
        var resultado = new Response<bool>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
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
        _unitOfWork.Completar();

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

    private string GenerarEmailUnico(string baseEmail, string rol)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = rol == "admin"
                ? CredentialsGenerator.BuildEmailAdmin(baseEmail, sufijo)
                : CredentialsGenerator.BuildSupportEmail(baseEmail, sufijo);

            var enUso = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Email == candidato).ReturnValue;
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }

    private string GenerarUsernameUnico(string nombreBase)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = CredentialsGenerator.GenerateUsername(nombreBase, sufijo);
            var enUso = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Username == candidato).ReturnValue;
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }
}
