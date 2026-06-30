namespace Homdutio.Api.Email;

/// <summary>
/// Development/test stand-in for <see cref="IEmailSender"/> selected when no ACS endpoint is
/// configured. Logs the link instead of sending so <c>dotnet run</c> and the integration-test
/// host never call Azure Communication Services. Reports success so callers behave identically to the
/// live path.
/// </summary>
public sealed class NoOpEmailSender(ILogger<NoOpEmailSender> logger) : IEmailSender
{
    public Task<bool> SendPasswordResetAsync(string recipientEmail, string resetLink, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email sending disabled (no ACS endpoint configured). Password-reset link for {Recipient}: {ResetLink}",
            recipientEmail,
            resetLink);

        return Task.FromResult(true);
    }

    public Task<bool> SendInviteAsync(string recipientEmail, string inviteLink, string householdName, string inviterName, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email sending disabled (no ACS endpoint configured). Invite to {Household} (from {Inviter}) for {Recipient}: {InviteLink}",
            householdName,
            inviterName,
            recipientEmail,
            inviteLink);

        return Task.FromResult(true);
    }
}
