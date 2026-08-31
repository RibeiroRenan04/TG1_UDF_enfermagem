using EstagioCheck.API.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EstagioCheck.API.Services;

public class TokenService(IConfiguration config)
{
    public string GenerateToken(ApplicationUser user)
    {
        var key = config["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT Key not configured.");
        var issuer = config["Jwt:Issuer"] ?? "EstagioCheck";
        var audience = config["Jwt:Audience"] ?? "EstagioCheckApp";
        var expiresHours = int.TryParse(config["Jwt:ExpiresInHours"], out var h) ? h : 8;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim("fullName", user.FullName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("mustChangePassword", user.MustChangePassword.ToString().ToLower()),
            new Claim("mustSetEmail", user.MustSetEmail.ToString().ToLower()),
            new Claim("mustAcceptTerms",
                (Roles.ExigeTermoResponsabilidade(user.Role) && user.TermsAcceptedAt == null)
                    .ToString().ToLower()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiresHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
