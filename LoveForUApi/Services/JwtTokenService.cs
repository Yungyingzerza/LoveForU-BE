using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LoveForU.Models;
using LoveForUApi.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LoveForUApi.Services;

public interface IJwtTokenService
{
    string GenerateToken(User user);
    CookieOptions BuildCookieOptions();
    string CookieName { get; }
}

internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("JWT signing key is not configured");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );
    }

    public string GenerateToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.ExpirationMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.displayName),
        };

        if (!string.IsNullOrWhiteSpace(user.pictureUrl))
        {
            claims.Add(new("picture", user.pictureUrl));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CookieName => _options.CookieName;

    public CookieOptions BuildCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.CookieSecure,
            SameSite = SameSiteMode.Strict,
            Domain = _options.CookieDomain,
            Expires = DateTimeOffset.UtcNow.AddMinutes(_options.ExpirationMinutes)
        };
    }
}
