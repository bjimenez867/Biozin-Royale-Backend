using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

// Compartido por AuthLN (perfiles de jugador) y StaffLN (staff admin/soporte): ambos
// firman el mismo tipo de JWT local, solo cambian los valores de los claims.
public static class JwtTokenFactory
{
    public static string GenerarToken(IConfiguration configuration, Guid id, string email, string role)
    {
        var llave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:LocalSigningKey"]!));
        var credenciales = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:LocalIssuer"],
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
