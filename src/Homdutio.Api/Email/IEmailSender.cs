namespace Homdutio.Api.Email;

/// <summary>
/// The transactional-email seam. v1 sends two messages — the password-reset email (S-08) and the
/// optional household-invite email — both built from embedded HTML templates via
/// <see cref="EmailTemplateRenderer"/>. Link construction stays in the endpoint layer (this seam never
/// composes URLs). The live Azure Communication Services implementation is swapped for
/// <see cref="NoOpEmailSender"/> in Development and tests so nothing user-facing requires a live provider.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends a password-reset email to <paramref name="recipientEmail"/> containing the fully-built
    /// <paramref name="resetLink"/> (link construction lives in the endpoint layer, not here).
    /// Returns <c>true</c> when the provider accepted the message, <c>false</c> on failure — the
    /// caller logs failures but still returns a generic 200 for enumeration safety.
    /// </summary>
    Task<bool> SendPasswordResetAsync(string recipientEmail, string resetLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a household-invite email to <paramref name="recipientEmail"/> with the fully-built
    /// <paramref name="inviteLink"/> (the <c>/join/&lt;token&gt;</c> URL composed by the endpoint), the
    /// <paramref name="householdName"/> they're invited to, and the <paramref name="inviterName"/> who
    /// invited them. Returns <c>true</c> when the provider accepted the message, <c>false</c> on failure —
    /// the caller swallows failures (the invite token is already minted; a failed mail isn't fatal).
    /// </summary>
    Task<bool> SendInviteAsync(string recipientEmail, string inviteLink, string householdName, string inviterName, CancellationToken cancellationToken = default);
}
