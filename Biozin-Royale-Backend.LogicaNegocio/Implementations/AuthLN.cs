using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class AuthLN : IAuthLN
{
    private const int MinPasswordLength = 8;

    private readonly IUnitWork _unitOfWork;
    private readonly IConfiguration _configuration;

    public AuthLN(IUnitWork unitOfWork, IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
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
            Balance = 1250.00m,
            Role = "user"
        };

        _unitOfWork.Profiles.Insertar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, GenerarToken(perfil));
        return resultado;
    }

    public Task<Response<TPerfilResultado>> LoginManualAsync(string email, string password)
    {
        var resultado = new Response<TPerfilResultado>();
        var emailNormalizado = email.Trim().ToLowerInvariant();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.Email == emailNormalizado).ReturnValue;
        if (perfil is null || perfil.Password is null || !BCrypt.Net.BCrypt.Verify(password, perfil.Password))
        {
            resultado.lpError("Credenciales inválidas", "El correo o la contraseña son incorrectos.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, GenerarToken(perfil));
        return Task.FromResult(resultado);
    }

    public async Task<Response<TPerfilResultado>> SincronizarOAuthAsync(Guid supabaseUserId, string? email, string? nombreCompleto, bool esAnonimo)
    {
        var resultado = new Response<TPerfilResultado>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == supabaseUserId).ReturnValue;
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
                Balance = 1250.00m,
                // El login social ya prueba que el usuario es dueño del correo (pasó por
                // el proveedor real), así que aquí sí se puede confiar en el dominio.
                Role = esAnonimo ? "user" : CredentialsGenerator.DetectRole(emailNormalizado)
            };

            _unitOfWork.Profiles.Insertar(perfil);
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
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, GenerarToken(perfil));
        return resultado;
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

    private string GenerarToken(Profile perfil)
    {
        var llave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:LocalSigningKey"]!));
        var credenciales = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, perfil.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, perfil.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", perfil.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:LocalIssuer"],
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
