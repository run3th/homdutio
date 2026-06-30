using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace Homdutio.Api.Email;

/// <summary>
/// Sends transactional email through Azure Communication Services Email. Both bodies (password reset and
/// household invite) are the hardened HTML templates rendered by <see cref="EmailTemplateRenderer"/>; the
/// sender composes the subject + a plain-text fallback and sends from the verified
/// <see cref="AcsEmailOptions.SenderAddress"/>. On a failed request it logs and returns <c>false</c> —
/// callers swallow the failure (reset still returns a generic 200 for enumeration safety; an invite token
/// is already minted). Registered only when an <see cref="AcsEmailOptions.Endpoint"/> is configured (auth
/// is by managed identity); otherwise <see cref="NoOpEmailSender"/> is used.
/// </summary>
public sealed class AcsEmailSender(
    EmailClient client,
    EmailTemplateRenderer renderer,
    IOptions<AcsEmailOptions> options,
    ILogger<AcsEmailSender> logger) : IEmailSender
{
    private readonly AcsEmailOptions _options = options.Value;

    public Task<bool> SendPasswordResetAsync(string recipientEmail, string resetLink, CancellationToken cancellationToken = default) =>
        SendAsync(BuildResetMessage(_options, renderer, recipientEmail, resetLink), cancellationToken);

    public Task<bool> SendInviteAsync(string recipientEmail, string inviteLink, string householdName, string inviterName, CancellationToken cancellationToken = default) =>
        SendAsync(BuildInviteMessage(_options, renderer, recipientEmail, inviteLink, householdName, inviterName), cancellationToken);

    private async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // WaitUntil.Started returns once ACS accepts the send request — we don't block the HTTP
            // request polling for terminal delivery status (transactional volume is tiny, no job queue).
            await client.SendAsync(WaitUntil.Started, message, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex)
        {
            // No body/recipient detail logged — the failure is operational, not user-facing.
            logger.LogError(ex, "Azure Communication Services rejected a transactional email (status {Status}).", ex.Status);
            return false;
        }
        catch (Exception ex)
        {
            // Any other send failure (credential acquisition, transport, timeout) is swallowed and reported
            // as failure too — a thrown exception would 500 a request whose response must not vary by outcome
            // (forgot-password's anti-enumeration 200; the invite endpoint's already-minted token).
            logger.LogError(ex, "Sending a transactional email failed.");
            return false;
        }
    }

    /// <summary>
    /// Builds the password-reset <see cref="EmailMessage"/> from the sender options, recipient, and
    /// pre-built link. The HTML body is the embedded <c>reset-password</c> template (its <c>{{reset_link}}</c>
    /// rendered + HTML-encoded by the renderer); a plain-text fallback carries the raw link. Pure and
    /// side-effect-free so the composition is unit-testable without a live ACS client.
    /// </summary>
    public static EmailMessage BuildResetMessage(AcsEmailOptions options, EmailTemplateRenderer renderer, string recipientEmail, string resetLink)
    {
        const string subject = "Reset your Homdutio password";

        var plainText =
            "We received a request to reset your Homdutio password.\n\n" +
            $"Use this link to choose a new password (valid for 1 hour):\n{resetLink}\n\n" +
            "If you didn't request this, you can safely ignore this email.";

        var html = renderer.Render("reset-password", new Dictionary<string, string>
        {
            ["reset_link"] = resetLink,
        });

        return Compose(options, recipientEmail, subject, plainText, html);
    }

    /// <summary>
    /// Builds the household-invite <see cref="EmailMessage"/>. The HTML body is the embedded
    /// <c>invite-member</c> template (its <c>{{invite_link}}</c>, <c>{{household_name}}</c>, and
    /// <c>{{inviter_name}}</c> rendered + HTML-encoded by the renderer); a plain-text fallback carries the
    /// raw link. Pure and side-effect-free for the same testability reason as the reset message.
    /// </summary>
    public static EmailMessage BuildInviteMessage(AcsEmailOptions options, EmailTemplateRenderer renderer, string recipientEmail, string inviteLink, string householdName, string inviterName)
    {
        var subject = $"You're invited to join {householdName} on Homdutio";

        var plainText =
            $"{inviterName} invited you to join {householdName} on Homdutio.\n\n" +
            $"Use this link to join (valid for 7 days):\n{inviteLink}\n\n" +
            "If you didn't expect this invite, you can safely ignore this email.";

        var html = renderer.Render("invite-member", new Dictionary<string, string>
        {
            ["invite_link"] = inviteLink,
            ["household_name"] = householdName,
            ["inviter_name"] = inviterName,
        });

        return Compose(options, recipientEmail, subject, plainText, html);
    }

    private static EmailMessage Compose(AcsEmailOptions options, string recipientEmail, string subject, string plainText, string html)
    {
        var content = new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html,
        };

        return new EmailMessage(options.SenderAddress, recipientEmail, content);
    }
}
