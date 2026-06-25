using System.Net;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace Homdutio.Api.Email;

/// <summary>
/// Sends the password-reset email through Azure Communication Services Email. Composes the subject
/// plus a plain-text and minimal HTML body carrying the reset link, sends from the verified
/// <see cref="AcsEmailOptions.SenderAddress"/>, and on a failed request logs and returns
/// <c>false</c> — the caller still returns a generic 200 (enumeration safety). Registered only when
/// an <see cref="AcsEmailOptions.Endpoint"/> is configured (auth is by managed identity); otherwise
/// <see cref="NoOpEmailSender"/> is used.
/// </summary>
public sealed class AcsEmailSender(
    EmailClient client,
    IOptions<AcsEmailOptions> options,
    ILogger<AcsEmailSender> logger) : IEmailSender
{
    private readonly AcsEmailOptions _options = options.Value;

    public async Task<bool> SendPasswordResetAsync(string recipientEmail, string resetLink, CancellationToken cancellationToken = default)
    {
        var message = BuildResetMessage(_options, recipientEmail, resetLink);

        try
        {
            // WaitUntil.Started returns once ACS accepts the send request — we don't block the HTTP
            // request polling for terminal delivery status (reset volume is tiny, no job queue).
            await client.SendAsync(WaitUntil.Started, message, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex)
        {
            // No body/recipient detail logged — the failure is operational, not user-facing.
            logger.LogError(ex, "Azure Communication Services rejected the password-reset email (status {Status}).", ex.Status);
            return false;
        }
        catch (Exception ex)
        {
            // Any other send failure (credential acquisition, transport, timeout) is swallowed and
            // reported as failure too — a thrown exception would 500 the forgot-password request only
            // for a known email, which is itself an account-enumeration signal.
            logger.LogError(ex, "Sending the password-reset email failed.");
            return false;
        }
    }

    /// <summary>
    /// Builds the password-reset <see cref="EmailMessage"/> from the sender options, recipient, and
    /// pre-built link. Pure and side-effect-free so the body/recipient composition is unit-testable
    /// without a live ACS client.
    /// </summary>
    public static EmailMessage BuildResetMessage(AcsEmailOptions options, string recipientEmail, string resetLink)
    {
        const string subject = "Reset your Homdutio password";

        var plainText =
            "We received a request to reset your Homdutio password.\n\n" +
            $"Use this link to choose a new password (valid for 1 hour):\n{resetLink}\n\n" +
            "If you didn't request this, you can safely ignore this email.";

        // HTML-encode the link before embedding it in markup — orthogonal to the caller's URL-encoding,
        // so this primitive is safe even if a future caller passes a less-sanitized link.
        var encodedLink = WebUtility.HtmlEncode(resetLink);
        var html =
            "<p>We received a request to reset your Homdutio password.</p>" +
            $"<p>Use this link to choose a new password (valid for 1 hour):<br>" +
            $"<a href=\"{encodedLink}\">{encodedLink}</a></p>" +
            "<p>If you didn't request this, you can safely ignore this email.</p>";

        var content = new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html,
        };

        return new EmailMessage(options.SenderAddress, recipientEmail, content);
    }
}
