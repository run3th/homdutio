namespace Homdutio.Api.Push;

/// <summary>
/// Strongly-typed VAPID settings bound from the <c>Vapid</c> configuration section, mirroring
/// <see cref="Homdutio.Api.Auth.JwtOptions"/>. <see cref="PublicKey"/> and <see cref="Subject"/> are
/// non-secret (safe to commit in appsettings.json — the public key is handed to every browser); the
/// <see cref="PrivateKey"/> is supplied out-of-band (user-secrets locally, App Service settings in prod)
/// and never committed. When no <see cref="PrivateKey"/> is configured the app registers the no-op sender
/// and <c>GET /api/push/key</c> reports push disabled, so local dev and tests need no keypair.
/// </summary>
public sealed class VapidOptions
{
    public const string SectionName = "Vapid";

    /// <summary>The VAPID public key (base64url) handed to the browser as the <c>applicationServerKey</c>.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>The VAPID private key — secret, injected out-of-band (never committed). Empty ⇒ push disabled.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>The VAPID subject: a <c>mailto:</c> or app URL identifying the sender to the push service.</summary>
    public string Subject { get; set; } = string.Empty;
}
