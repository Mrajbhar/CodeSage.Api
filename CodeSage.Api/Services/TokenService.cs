using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CodeSage.Api.Models;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CodeSage.Api.Services;

public class TokenService
{
    private readonly JwtSettings _jwt;
    private readonly SymmetricSecurityKey _key;

    public TokenService(IOptions<JwtSettings> jwt)
    {
        _jwt = jwt.Value;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
    }

    public string CreateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id!),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("displayName", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshToken CreateRefreshToken() => new()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays)
    };

    // --- OAuth "state": a short-lived signed token used for CSRF protection and,
    //     when linking, to carry the id of the already-logged-in user. ---
    public string CreateStateToken(string? linkUserId)
    {
        var claims = new List<Claim>
        {
            new("purpose", "oauth_state"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (linkUserId is not null) claims.Add(new("link_user", linkUserId));

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer, audience: _jwt.Audience, claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (bool ok, string? linkUserId) ValidateStateToken(string state)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(state, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwt.Issuer, ValidAudience = _jwt.Audience, IssuerSigningKey = _key,
                ClockSkew = TimeSpan.FromSeconds(10)
            }, out _);

            if (principal.FindFirst("purpose")?.Value != "oauth_state") return (false, null);
            return (true, principal.FindFirst("link_user")?.Value);
        }
        catch
        {
            return (false, null);
        }
    }
}
