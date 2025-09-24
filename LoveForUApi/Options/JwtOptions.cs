namespace LoveForUApi.Options;

public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
    public string CookieName { get; set; } = "loveforu_auth";
    public bool CookieSecure { get; set; } = true;
    public string? CookieDomain { get; set; }
}
