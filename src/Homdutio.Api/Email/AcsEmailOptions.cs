namespace Homdutio.Api.Email;

/// <summary>
/// Strongly-typed Azure Communication Services (ACS) Email settings bound from the <c>AcsEmail</c>
/// configuration section, mirroring <see cref="Auth.JwtOptions"/>. Both values are non-secret:
/// <see cref="SenderAddress"/> is the verified MailFrom address on the connected email domain, and
/// <see cref="Endpoint"/> is the ACS resource URL. Authentication is by Microsoft Entra ID /
/// managed identity (<c>DefaultAzureCredential</c>) — no connection string or access key anywhere.
/// Both are resource-specific, so they're set per environment (user-secrets / appsettings.Development
/// locally, App Service settings in prod) rather than committed. When no <see cref="Endpoint"/> is
/// configured the host registers <see cref="NoOpEmailSender"/>, so <c>dotnet run</c> works without an
/// ACS resource.
/// </summary>
public sealed class AcsEmailOptions
{
    public const string SectionName = "AcsEmail";

    /// <summary>The verified sender address on the connected domain, e.g. <c>DoNotReply@&lt;domain&gt;.azurecomm.net</c>.</summary>
    public string SenderAddress { get; set; } = string.Empty;

    /// <summary>ACS resource endpoint, e.g. <c>https://&lt;resource&gt;.communication.azure.com/</c>. Absence selects the no-op sender.</summary>
    public string Endpoint { get; set; } = string.Empty;
}
