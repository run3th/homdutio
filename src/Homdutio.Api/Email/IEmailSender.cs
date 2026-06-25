namespace Homdutio.Api.Email;

/// <summary>
/// The narrow email seam the password-reset flow depends on. Deliberately reset-shaped (not a
/// general email API): password reset is the only permitted v1 transactional email (S-08). The live
/// Azure Communication Services implementation is swapped for <see cref="NoOpEmailSender"/> in
/// Development and tests so nothing user-facing requires a live provider.
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
}
