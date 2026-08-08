using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class AuthLN : IAuthLN
{
    private const int MinPasswordLength = 8;
    private const int MaxIntentosFallidos = 5;
    private const int MinutosBloqueo = 15;

    private static string GenerarCodigoSeguro() => RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();

    // Bloqueo temporal por fuerza bruta (login y 2FA comparten el mismo contador:
    // ambos son intentos de autenticarse en la misma cuenta).
    private static bool EstaBloqueado(Profile perfil, out string mensaje)
    {
        if (perfil.LockedUntil is not null && perfil.LockedUntil > DateTime.UtcNow)
        {
            var minutos = Math.Max(1, (int)Math.Ceiling((perfil.LockedUntil.Value - DateTime.UtcNow).TotalMinutes));
            mensaje = $"Demasiados intentos fallidos. Intenta de nuevo en {minutos} minuto(s).";
            return true;
        }
        mensaje = string.Empty;
        return false;
    }

    private void RegistrarIntentoFallido(Profile perfil)
    {
        perfil.FailedLoginAttempts++;
        if (perfil.FailedLoginAttempts >= MaxIntentosFallidos)
        {
            perfil.LockedUntil = DateTime.UtcNow.AddMinutes(MinutosBloqueo);
            perfil.FailedLoginAttempts = 0;
            RegistrarEvento(perfil.Id, "account_locked");
        }
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();
    }

    private void RestablecerIntentosFallidos(Profile perfil)
    {
        if (perfil.FailedLoginAttempts == 0 && perfil.LockedUntil is null) return;

        perfil.FailedLoginAttempts = 0;
        perfil.LockedUntil = null;
        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();
    }

    // Overloads para StaffMember: RestablecerPasswordAsync maneja el reset de staff
    // directamente (no delega en StaffLN), así que necesita su propio bloqueo aquí.
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
            _unitOfWork.SecurityEvents.Insertar(new SecurityEvent
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                EventType = "account_locked",
                CreatedAt = DateTime.UtcNow,
            });
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

    private readonly IUnitWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IStaffLN _staffLN;
    private readonly IEmailService _emailService;

    public AuthLN(IUnitWork unitOfWork, IConfiguration configuration, IStaffLN staffLN, IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _staffLN = staffLN;
        _emailService = emailService;
    }

    public async Task<Response<TPerfilResultado>> RegistrarManualAsync(TRegistroManual datos)
    {
        var resultado = new Response<TPerfilResultado>();

        if (datos.Password != datos.Confirm)
        {
            resultado.lpError("Datos inválidos", "La contraseña y la confirmación no coinciden.");
            return resultado;
        }

        if (datos.Password.Length < MinPasswordLength)
        {
            resultado.lpError("Datos inválidos", $"La contraseña debe tener al menos {MinPasswordLength} caracteres.");
            return resultado;
        }

        var email = datos.Email.Trim().ToLowerInvariant();

        // El registro manual no verifica que el usuario sea dueño del correo (no hay
        // confirmación por email), así que no puede confiarse en el dominio para dar
        // rol admin/soporte aquí: cualquiera podría escribir ese dominio en el formulario.
        if (CredentialsGenerator.DetectRole(email) != "user")
        {
            resultado.lpError("Dominio no permitido", "No puedes registrarte con este dominio de correo.");
            return resultado;
        }

        var existente = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == email);
        if (existente.ReturnValue is not null)
        {
            resultado.lpError("Correo en uso", "Ya existe una cuenta registrada con este correo.");
            return resultado;
        }

        // Puede haber una fila en auth.users sin Profile (ej. un login con Google que
        // nunca llegó a sincronizarse). Insertar de nuevo violaría la restricción de
        // correo único de Supabase Auth, así que lo detectamos antes y avisamos.
        if (await _unitOfWork.ExisteUsuarioAuthAsync(email))
        {
            resultado.lpError("Correo en uso", "Ya existe una cuenta asociada a este correo. Intenta iniciar sesión, incluso con Google.");
            return resultado;
        }

        var username = GenerarUsernameUnico(datos.Nombre);
        var id = Guid.NewGuid();
        var ahora = DateTime.UtcNow;

        await _unitOfWork.InsertarUsuarioAuthAsync(id, email);

        var perfil = new Profile
        {
            Id = Guid.NewGuid(),
            UserId = id,
            Username = username,
            DisplayName = datos.Nombre,
            IsGuest = false,
            Status = "active",
            CreatedAt = ahora,
            UpdatedAt = ahora,
            Phone = datos.Phone,
            Email = email,
            Country = PhoneCountryLookup.GetCountry(datos.Phone),
            Password = BCrypt.Net.BCrypt.HashPassword(datos.Password),
            EmailVerified = false,
        };

        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            UserId = id,
            Balance = 0.00m,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        _unitOfWork.Profiles.Insertar(perfil);
        _unitOfWork.Wallets.Insertar(wallet);
        _unitOfWork.Completar();

        // Enviar código de verificación (no bloqueante)
        await EnviarVerificacionAsync(email);

        // Devuelve perfil sin token: el usuario aún no está autenticado hasta verificar
        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null);
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> LoginManualAsync(string email, string password, string? userAgent, string? ipAddress)
    {
        var resultado = new Response<TPerfilResultado>();
        var emailNormalizado = email.Trim().ToLowerInvariant();

        // El dominio del correo enruta el login a la tabla de staff (admin/soporte) en
        // vez de profiles: el rol de staff nunca se infiere del dominio, solo lo define
        // un admin al crear el miembro; el dominio aquí solo decide a qué tabla mirar.
        if (CredentialsGenerator.DetectRole(emailNormalizado) != "user")
        {
            return await _staffLN.LoginAsync(emailNormalizado, password, userAgent, ipAddress);
        }

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNormalizado).ReturnValue;
        var credencialesValidas = perfil is not null && perfil.Password is not null
            && BCrypt.Net.BCrypt.Verify(password, perfil.Password);

        if (!credencialesValidas)
        {
            // Si ya está bloqueada no se sigue contando: el bloqueo tiene duración fija.
            if (perfil is not null && perfil.Password is not null && !EstaBloqueado(perfil, out _))
            {
                RegistrarIntentoFallido(perfil);
            }
            resultado.lpError("Credenciales inválidas", "El correo o la contraseña son incorrectos.");
            return resultado;
        }

        // Mismo título "Cuenta bloqueada" que el bloqueo administrativo: el frontend
        // ya sabe mostrarlo en el banner dedicado sin distinguir el origen.
        if (EstaBloqueado(perfil!, out var mensajeBloqueo))
        {
            resultado.lpError("Cuenta bloqueada", mensajeBloqueo);
            return resultado;
        }

        RestablecerIntentosFallidos(perfil!);

        if (!perfil!.EmailVerified)
        {
            resultado.lpError("Email no verificado", "Debes verificar tu correo electrónico antes de iniciar sesión.");
            return resultado;
        }

        var bloqueoActivo = _unitOfWork.UserBlocks
            .ObtenerEntidad(b => b.ProfileId == perfil.Id && b.IsActive).ReturnValue;
        if (bloqueoActivo is not null)
        {
            resultado.lpError("Cuenta bloqueada", bloqueoActivo.Message);
            return resultado;
        }

        if (perfil.TwoFactorEnabled)
        {
            await GenerarYEnviarCodigo2FAAsync(perfil);
            resultado.lpError("2FA requerido", "Te enviamos un código de verificación a tu correo.");
            return resultado;
        }

        RegistrarEvento(perfil.Id, "login");
        var (loginToken, loginRefresh) = GenerarTokenConSesion(perfil, userAgent, ipAddress);
        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, loginToken, loginRefresh);
        return resultado;
    }

    private async Task GenerarYEnviarCodigo2FAAsync(Profile perfil)
    {
        var remitente = _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com";

        var codigo = GenerarCodigoSeguro();
        perfil.TwoFactorCode = BCrypt.Net.BCrypt.HashPassword(codigo);
        perfil.TwoFactorCodeExpiresAt = DateTime.UtcNow.AddMinutes(10);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        try
        {
            await _emailService.EnviarCodigo2FAAsync(
                correoDestino: perfil.Email,
                nombre: perfil.DisplayName ?? perfil.Username,
                codigo: codigo,
                correoRemitente: remitente
            );
            Console.WriteLine($"[2FA] Código enviado a {perfil.Email}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[2FA] Error enviando correo a {perfil.Email}: {ex.GetType().Name} — {ex.Message}");
        }
    }

    public async Task<Response<TPerfilResultado>> VerificarCodigo2FAAsync(string email, string code, string? userAgent, string? ipAddress)
    {
        var resultado = new Response<TPerfilResultado>();
        var emailNorm = email.Trim().ToLowerInvariant();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNorm).ReturnValue;

        if (perfil is null || string.IsNullOrEmpty(perfil.TwoFactorCode) || perfil.TwoFactorCodeExpiresAt is null)
        {
            resultado.lpError("Código inválido", "El código no es válido o ya fue usado.");
            return resultado;
        }

        if (EstaBloqueado(perfil, out var mensajeBloqueo))
        {
            resultado.lpError("Cuenta bloqueada", mensajeBloqueo);
            return resultado;
        }

        if (DateTime.UtcNow > perfil.TwoFactorCodeExpiresAt)
        {
            resultado.lpError("Código expirado", "El código de verificación ha expirado. Solicita uno nuevo.");
            return resultado;
        }

        if (!BCrypt.Net.BCrypt.Verify(code.Trim(), perfil.TwoFactorCode))
        {
            RegistrarIntentoFallido(perfil);
            resultado.lpError("Código incorrecto", "El código ingresado no es correcto.");
            return resultado;
        }

        RestablecerIntentosFallidos(perfil);

        perfil.TwoFactorCode = null;
        perfil.TwoFactorCodeExpiresAt = null;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        RegistrarEvento(perfil.Id, "login");
        var (twoFaToken, twoFaRefresh) = GenerarTokenConSesion(perfil, userAgent, ipAddress);
        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, twoFaToken, twoFaRefresh);
        return resultado;
    }

    public async Task<Response<bool>> ReenviarCodigo2FAAsync(string email)
    {
        var resultado = new Response<bool>();
        var emailNorm = email.Trim().ToLowerInvariant();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNorm).ReturnValue;
        if (perfil is not null && perfil.TwoFactorEnabled)
        {
            await GenerarYEnviarCodigo2FAAsync(perfil);
        }

        // No revela si el correo existe o si tiene 2FA activo, igual que SolicitarRecuperacionAsync
        resultado.ReturnValue = true;
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> SincronizarOAuthAsync(Guid supabaseUserId, string? email, string? nombreCompleto, bool esAnonimo, Guid? sessionId, string? userAgent, string? ipAddress)
    {
        var resultado = new Response<TPerfilResultado>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == supabaseUserId).ReturnValue;
        if (perfil is not null)
        {
            var bloqueoActivo = _unitOfWork.UserBlocks
                .ObtenerEntidad(b => b.ProfileId == perfil.Id && b.IsActive).ReturnValue;
            if (bloqueoActivo is not null)
            {
                resultado.lpError("Cuenta bloqueada", bloqueoActivo.Message);
                return resultado;
            }
        }

        if (perfil is null)
        {
            var ahora = DateTime.UtcNow;

            // Las sesiones anónimas de Supabase no traen email: se genera uno único y
            // determinístico solo para satisfacer la columna no-nula de profiles.
            var emailNormalizado = esAnonimo
                ? $"guest-{supabaseUserId:N}@guest.biozin.cr"
                : email!.Trim().ToLowerInvariant();

            perfil = new Profile
            {
                Id = Guid.NewGuid(),
                UserId = supabaseUserId,
                Username = GenerarUsernameUnico(esAnonimo ? "Invitado" : (nombreCompleto ?? email!.Split('@')[0])),
                DisplayName = nombreCompleto,
                IsGuest = esAnonimo,
                Status = "active",
                CreatedAt = ahora,
                UpdatedAt = ahora,
                Email = emailNormalizado,
                Password = null,
                // OAuth verifica la identidad del usuario vía proveedor; los invitados no tienen correo real
                EmailVerified = !esAnonimo,
            };

            var wallet = new Wallet
            {
                Id = Guid.NewGuid(),
                UserId = supabaseUserId,
                Balance = 1250.00m,
                CreatedAt = ahora,
                UpdatedAt = ahora,
            };

            _unitOfWork.Profiles.Insertar(perfil);
            _unitOfWork.Wallets.Insertar(wallet);
            _unitOfWork.Completar();
        }

        // El JWT lo emite Supabase, no nosotros — a diferencia del login manual, aquí no
        // hay un jti propio que registrar. Supabase sí incluye un claim "session_id"
        // estable entre refrescos del mismo login, así que se usa ese como Id de Session
        // para poder listar/revocar también las sesiones de Google desde "Sesiones activas".
        if (sessionId.HasValue && _unitOfWork.Sessions.ObtenerEntidad(s => s.Id == sessionId.Value).ReturnValue is null)
        {
            _unitOfWork.Sessions.Insertar(new Session
            {
                Id = sessionId.Value,
                ProfileId = perfil.Id,
                DeviceLabel = DeviceParser.AnalizarUserAgent(userAgent),
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
            });
            // Los invitados no tienen cuenta real que auditar todavía.
            if (!esAnonimo) RegistrarEvento(perfil.Id, "login");
            _unitOfWork.Completar();
        }

        resultado.ReturnValue = await Task.FromResult(PerfilMapper.MapearPerfil(perfil, token: null));
        return resultado;
    }

    public async Task<Response<TPerfilResultado>> ReclamarInvitadoAsync(Guid userId, TRegistroManual datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null || !perfil.IsGuest)
        {
            resultado.lpError("Cuenta inválida", "Esta cuenta no es de invitado.");
            return resultado;
        }

        if (datos.Password != datos.Confirm)
        {
            resultado.lpError("Datos inválidos", "La contraseña y la confirmación no coinciden.");
            return resultado;
        }

        if (datos.Password.Length < MinPasswordLength)
        {
            resultado.lpError("Datos inválidos", $"La contraseña debe tener al menos {MinPasswordLength} caracteres.");
            return resultado;
        }

        var email = datos.Email.Trim().ToLowerInvariant();

        // Mismo bloqueo que el registro normal: reclamar un invitado tampoco prueba
        // que el usuario sea dueño del correo.
        if (CredentialsGenerator.DetectRole(email) != "user")
        {
            resultado.lpError("Dominio no permitido", "No puedes registrarte con este dominio de correo.");
            return resultado;
        }

        var existente = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == email && p.UserId != userId);
        if (existente.ReturnValue is not null)
        {
            resultado.lpError("Correo en uso", "Ya existe una cuenta registrada con este correo.");
            return resultado;
        }

        if (await _unitOfWork.ExisteUsuarioAuthAsync(email))
        {
            resultado.lpError("Correo en uso", "Ya existe una cuenta asociada a este correo. Intenta iniciar sesión, incluso con Google.");
            return resultado;
        }

        perfil.Username = GenerarUsernameUnico(datos.Nombre);
        perfil.DisplayName = datos.Nombre;
        perfil.Phone = datos.Phone;
        perfil.Country = PhoneCountryLookup.GetCountry(datos.Phone);
        perfil.Email = email;
        perfil.Password = BCrypt.Net.BCrypt.HashPassword(datos.Password);
        perfil.IsGuest = false;
        perfil.EmailVerified = false;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        await EnviarVerificacionAsync(email);

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null);
        return resultado;
    }

    public async Task<Response<bool>> SolicitarRecuperacionAsync(string email)
    {
        var resultado = new Response<bool>();
        var emailNorm = email.Trim().ToLowerInvariant();
        var remitente = _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com";

        // Correos de staff (@admin.biozin.cr / @support.biozin.cr) viven en staff_members
        if (CredentialsGenerator.DetectRole(emailNorm) != "user")
        {
            var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Email == emailNorm).ReturnValue;

            if (staff is null)
            {
                Console.Error.WriteLine($"[Recuperación] Staff no encontrado: {emailNorm}");
                resultado.ReturnValue = true;
                return resultado;
            }

            var codigoStaff = GenerarCodigoSeguro();
            staff.ResetCode = BCrypt.Net.BCrypt.HashPassword(codigoStaff);
            staff.ResetCodeExpiresAt = DateTime.UtcNow.AddMinutes(15);
            staff.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.StaffMembers.Modificar(staff);
            _unitOfWork.Completar();

            Console.WriteLine($"[Recuperación] Código generado para staff {emailNorm}. Enviando correo...");

            try
            {
                await _emailService.EnviarCodigoRecuperacionAsync(
                    correoDestino: emailNorm,
                    nombre: staff.DisplayName,
                    codigo: codigoStaff,
                    correoRemitente: remitente
                );
                Console.WriteLine($"[Recuperación] Correo enviado exitosamente a {emailNorm}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Recuperación] Error enviando correo a {emailNorm}: {ex.GetType().Name} — {ex.Message}");
            }

            resultado.ReturnValue = true;
            return resultado;
        }

        // Usuarios normales → profiles
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNorm).ReturnValue;

        if (perfil is null)
        {
            Console.Error.WriteLine($"[Recuperación] Correo no encontrado: {emailNorm}");
            resultado.ReturnValue = true;
            return resultado;
        }

        if (string.IsNullOrEmpty(perfil.Password))
        {
            Console.Error.WriteLine($"[Recuperación] El perfil {emailNorm} no tiene contraseña (cuenta OAuth).");
            resultado.ReturnValue = true;
            return resultado;
        }

        var codigo = GenerarCodigoSeguro();
        perfil.ResetCode = BCrypt.Net.BCrypt.HashPassword(codigo);
        perfil.ResetCodeExpiresAt = DateTime.UtcNow.AddMinutes(15);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        Console.WriteLine($"[Recuperación] Código generado para {emailNorm}. Enviando correo...");

        try
        {
            await _emailService.EnviarCodigoRecuperacionAsync(
                correoDestino: emailNorm,
                nombre: perfil.DisplayName ?? perfil.Username,
                codigo: codigo,
                correoRemitente: remitente
            );
            Console.WriteLine($"[Recuperación] Correo enviado exitosamente a {emailNorm}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Recuperación] Error enviando correo a {emailNorm}: {ex.GetType().Name} — {ex.Message}");
        }

        resultado.ReturnValue = true;
        return resultado;
    }

    public Task<Response<bool>> RestablecerPasswordAsync(string email, string code, string newPassword)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            resultado.lpError("Contraseña inválida", "La nueva contraseña debe tener al menos 8 caracteres.");
            return Task.FromResult(resultado);
        }

        var emailNorm = email.Trim().ToLowerInvariant();

        // Correos de staff
        if (CredentialsGenerator.DetectRole(emailNorm) != "user")
        {
            var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Email == emailNorm).ReturnValue;

            if (staff is null || string.IsNullOrEmpty(staff.ResetCode) || staff.ResetCodeExpiresAt is null)
            {
                resultado.lpError("Código inválido", "El código no es válido o ya fue usado.");
                return Task.FromResult(resultado);
            }

            if (EstaBloqueado(staff, out var mensajeBloqueoStaff))
            {
                resultado.lpError("Cuenta bloqueada", mensajeBloqueoStaff);
                return Task.FromResult(resultado);
            }

            if (DateTime.UtcNow > staff.ResetCodeExpiresAt)
            {
                resultado.lpError("Código expirado", "El código de recuperación ha expirado. Solicita uno nuevo.");
                return Task.FromResult(resultado);
            }

            if (!BCrypt.Net.BCrypt.Verify(code.Trim(), staff.ResetCode))
            {
                RegistrarIntentoFallido(staff);
                resultado.lpError("Código incorrecto", "El código ingresado no es correcto.");
                return Task.FromResult(resultado);
            }

            RestablecerIntentosFallidos(staff);

            staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            staff.ResetCode = null;
            staff.ResetCodeExpiresAt = null;
            staff.MustChangePassword = false;
            staff.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.StaffMembers.Modificar(staff);
            _unitOfWork.Completar();

            resultado.ReturnValue = true;
            return Task.FromResult(resultado);
        }

        // Usuarios normales
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNorm).ReturnValue;

        if (perfil is null || string.IsNullOrEmpty(perfil.ResetCode) || perfil.ResetCodeExpiresAt is null)
        {
            resultado.lpError("Código inválido", "El código no es válido o ya fue usado.");
            return Task.FromResult(resultado);
        }

        if (EstaBloqueado(perfil, out var mensajeBloqueoPerfil))
        {
            resultado.lpError("Cuenta bloqueada", mensajeBloqueoPerfil);
            return Task.FromResult(resultado);
        }

        if (DateTime.UtcNow > perfil.ResetCodeExpiresAt)
        {
            resultado.lpError("Código expirado", "El código de recuperación ha expirado. Solicita uno nuevo.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(code.Trim(), perfil.ResetCode))
        {
            RegistrarIntentoFallido(perfil);
            resultado.lpError("Código incorrecto", "El código ingresado no es correcto.");
            return Task.FromResult(resultado);
        }

        RestablecerIntentosFallidos(perfil);

        perfil.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        perfil.ResetCode = null;
        perfil.ResetCodeExpiresAt = null;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    private string GenerarUsernameUnico(string nombreBase)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = CredentialsGenerator.GenerateUsername(nombreBase, sufijo);
            var enUso = _unitOfWork.Profiles.ObtenerEntidad(p => p.Username == candidato).ReturnValue;
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }

    public async Task<Response<bool>> EnviarVerificacionAsync(string email)
    {
        var resultado = new Response<bool>();
        var emailNorm = email.Trim().ToLowerInvariant();
        var remitente = _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com";

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNorm).ReturnValue;
        if (perfil is null || perfil.EmailVerified)
        {
            resultado.ReturnValue = true;
            return resultado;
        }

        var codigo = GenerarCodigoSeguro();
        perfil.VerifyCode = BCrypt.Net.BCrypt.HashPassword(codigo);
        perfil.VerifyCodeExpiresAt = DateTime.UtcNow.AddMinutes(15);
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        try
        {
            await _emailService.EnviarVerificacionEmailAsync(
                correoDestino: emailNorm,
                nombre: perfil.DisplayName ?? perfil.Username,
                codigo: codigo,
                correoRemitente: remitente
            );
            Console.WriteLine($"[Verificación] Código enviado a {emailNorm}.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Verificación] Error enviando correo a {emailNorm}: {ex.GetType().Name} — {ex.Message}");
        }

        resultado.ReturnValue = true;
        return resultado;
    }

    public Task<Response<bool>> VerificarEmailAsync(string email, string code)
    {
        var resultado = new Response<bool>();
        var emailNorm = email.Trim().ToLowerInvariant();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNorm).ReturnValue;

        if (perfil is null || string.IsNullOrEmpty(perfil.VerifyCode) || perfil.VerifyCodeExpiresAt is null)
        {
            resultado.lpError("Código inválido", "El código no es válido o ya fue usado.");
            return Task.FromResult(resultado);
        }

        if (DateTime.UtcNow > perfil.VerifyCodeExpiresAt)
        {
            resultado.lpError("Código expirado", "El código de verificación ha expirado. Solicita uno nuevo.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(code.Trim(), perfil.VerifyCode))
        {
            resultado.lpError("Código incorrecto", "El código ingresado no es correcto.");
            return Task.FromResult(resultado);
        }

        perfil.EmailVerified = true;
        perfil.VerifyCode = null;
        perfil.VerifyCodeExpiresAt = null;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    // No hace su propio Completar(): se inserta junto con el resto de cambios del
    // método que la llama (mismo patrón que la versión en ProfileLN).
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

    // A diferencia de GenerarToken (usado para OAuth/staff, sin sesión rastreable),
    // este helper es para los dos únicos flujos donde emitimos el JWT nosotros mismos
    // (login manual y verificación de 2FA): genera un sessionId estable (Session.Id) y
    // un jti por-token separados; devuelve (accessToken, refreshToken).
    private (string token, string refreshToken) GenerarTokenConSesion(Profile perfil, string? userAgent, string? ipAddress)
    {
        var sessionId = Guid.NewGuid();
        var jti = Guid.NewGuid();
        var token = JwtTokenFactory.GenerarToken(_configuration, perfil.UserId, perfil.Email, "user", jti, sessionId);

        var refreshRaw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshRaw)));

        _unitOfWork.Sessions.Insertar(new Session
        {
            Id = sessionId,
            ProfileId = perfil.Id,
            DeviceLabel = DeviceParser.AnalizarUserAgent(userAgent),
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            RefreshTokenHash = refreshHash,
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        _unitOfWork.Completar();

        return (token, refreshRaw);
    }

    public Task<Response<TTokenPar>> RefreshAsync(string refreshToken)
    {
        var resultado = new Response<TTokenPar>();

        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var sesion = _unitOfWork.Sessions.ObtenerEntidad(s =>
            s.RefreshTokenHash == hash && s.IsActive).ReturnValue;

        if (sesion is null || sesion.RefreshTokenExpiresAt is null || sesion.RefreshTokenExpiresAt < DateTime.UtcNow)
        {
            resultado.lpError("Token inválido", "El refresh token no es válido o ha expirado.");
            return Task.FromResult(resultado);
        }

        Guid userId;
        string email;
        string role;

        if (sesion.ProfileId.HasValue)
        {
            var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Id == sesion.ProfileId.Value).ReturnValue;
            if (perfil is null || perfil.Status != "active")
            {
                resultado.lpError("Cuenta inactiva", "La cuenta asociada a esta sesión no está activa.");
                return Task.FromResult(resultado);
            }
            userId = perfil.UserId;
            email = perfil.Email;
            role = "user";
        }
        else if (sesion.StaffId.HasValue)
        {
            var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == sesion.StaffId.Value).ReturnValue;
            if (staff is null || staff.Status != "active")
            {
                resultado.lpError("Cuenta inactiva", "La cuenta asociada a esta sesión no está activa.");
                return Task.FromResult(resultado);
            }
            userId = staff.Id;
            email = staff.Email;
            role = CredentialsGenerator.DetectRole(staff.Email);
        }
        else
        {
            resultado.lpError("Token inválido", "La sesión no tiene usuario asociado.");
            return Task.FromResult(resultado);
        }

        // Rotación: nuevo access token (nuevo jti, mismo session_id) + nuevo refresh token
        var newJti = Guid.NewGuid();
        var newAccessToken = JwtTokenFactory.GenerarToken(_configuration, userId, email, role, newJti, sesion.Id);

        var newRefreshRaw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newRefreshHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshRaw)));

        sesion.RefreshTokenHash = newRefreshHash;
        sesion.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        _unitOfWork.Sessions.Modificar(sesion);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TTokenPar { Token = newAccessToken, RefreshToken = newRefreshRaw };
        return Task.FromResult(resultado);
    }
}
