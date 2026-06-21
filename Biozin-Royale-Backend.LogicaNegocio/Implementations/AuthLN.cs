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
            Password = BCrypt.Net.BCrypt.HashPassword(datos.Password)
        };

        _unitOfWork.Profiles.Insertar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = MapearPerfil(perfil, GenerarToken(perfil));
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

        resultado.ReturnValue = MapearPerfil(perfil, GenerarToken(perfil));
        return Task.FromResult(resultado);
    }

    public async Task<Response<TPerfilResultado>> SincronizarOAuthAsync(Guid supabaseUserId, string email, string? nombreCompleto)
    {
        var resultado = new Response<TPerfilResultado>();

        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == supabaseUserId).ReturnValue;
        if (perfil is null)
        {
            var ahora = DateTime.UtcNow;
            perfil = new Profile
            {
                Id = Guid.NewGuid(),
                UserId = supabaseUserId,
                Username = GenerarUsernameUnico(nombreCompleto ?? email.Split('@')[0]),
                DisplayName = nombreCompleto,
                IsGuest = false,
                Status = "active",
                CreatedAt = ahora,
                UpdatedAt = ahora,
                Email = email.Trim().ToLowerInvariant(),
                Password = null
            };

            _unitOfWork.Profiles.Insertar(perfil);
            _unitOfWork.Completar();
        }

        resultado.ReturnValue = await Task.FromResult(MapearPerfil(perfil, token: null));
        return resultado;
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

        resultado.ReturnValue = MapearPerfil(perfil, token: null);
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

        resultado.ReturnValue = MapearPerfil(perfil, token: null);
        return Task.FromResult(resultado);
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
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:LocalIssuer"],
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TPerfilResultado MapearPerfil(Profile perfil, string? token)
    {
        var camposPendientes = new List<string>();
        if (string.IsNullOrWhiteSpace(perfil.Phone)) camposPendientes.Add("phone");
        if (string.IsNullOrWhiteSpace(perfil.Country)) camposPendientes.Add("country");
        if (perfil.Birthdate is null) camposPendientes.Add("birthdate");

        return new TPerfilResultado
        {
            Id = perfil.UserId,
            Username = perfil.Username,
            DisplayName = perfil.DisplayName,
            Email = perfil.Email,
            Phone = perfil.Phone,
            Country = perfil.Country,
            Birthdate = perfil.Birthdate,
            Status = perfil.Status,
            Token = token,
            CamposPendientes = camposPendientes
        };
    }
}
