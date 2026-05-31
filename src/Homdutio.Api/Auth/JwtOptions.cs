namespace Homdutio.Api.Auth;

/// <summary>
/// Strongly-typed JWT settings bound from the <c>Jwt</c> configuration section. Issuer, Audience, and
/// AccessTokenMinutes are non-secret (committed in appsettings.json); SigningKey is supplied out-of-band
/// (user-secrets locally, App Service settings in prod) and never committed.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 120;
}
