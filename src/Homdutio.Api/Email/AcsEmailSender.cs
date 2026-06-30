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

        var message = new EmailMessage(options.SenderAddress, recipientEmail, content);

        // The brand logo is sent as an inline attachment referenced by the templates as
        // <img src="cid:homdutio-logo">, not a data: URI. Gmail and other webmail clients strip
        // data: image sources from email bodies, so an inline (Content-ID) attachment is the only
        // reliable way to render the mark without depending on a publicly hosted asset.
        message.Attachments.Add(new EmailAttachment(
            "homdutio-logo.png",
            "image/png",
            BinaryData.FromBytes(Convert.FromBase64String(LogoPngBase64)))
        {
            ContentId = "homdutio-logo",
        });

        return message;
    }

    // White house-mark on the brand green tile, 88x88 PNG — the same asset the templates previously
    // inlined as a data: URI, now attached inline (see Compose) so it survives webmail sanitizers.
    private const string LogoPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAFgAAABYCAYAAABxlTA0AAAEMElEQVR4AezbAXabMAyA4WYXW3uypidrd7JMP7NfeHkhlowFgmkPz6QIY39o2E2yX2/5x1UggV15394SOIGdBZybzwxOYGcB5+YzgxPYWcC5+czgBHYWcG4+MziB7QKRzsgMdr4bCZzAd4Hb7fZ+f3WMvUNkMLBSvoX0W2o26qu8Dr+FBxZNIMGdZy/7n+VYaOTQwAXw84VgeOSwwArc6h4aOSSw4PJIeJW5FbfWIHNOfR2mDgUssO9SgOIZa0WazpXze861XksdHwa4wPTi1gGDywqDuv5s1zoE8Ax3FAbIrD5GtdfdzjDg3h4ILhBkrqaJLwmiSNXceC7TdjPQM2BX4IKrncy+LpfLlSIgh0HeDbgHV2Cn7UjIuwALLo8EU+ZOsrO/joK8OXDB1c7yHwVyRnvfLcc+7j95ucczmRv7Mmj0wc2ABZZ16k0GoMH9kThwqWV3eRNkYrTIUx/oy3KLY49sAlwGpM2eH0FT4VYKieeci7wGW6rmttkyzh1YcFkqmXCbPAsBAk0mh1phuAIXXMtkBtACn+7HgswNDYPsBtyBC4xOsREVCdkFWHB5JFgydxhutY+CPBy44GpWClgwmQ3HpWFKBzKJwan/yoC/hwELLEsgOmjB1c763UMtyNpn+zQGxtJ9wYcThwCXDmlxQSVzqR+64/NSkC3LOBKEZRz16g6tBhZc/omDq+kMA90Ud94pgSaTtTcWZMY2b8K8vwq44FomMwZo7uTIEwryZsu4buAO3NXZMApakOnLJshdwEfGrTdpK2QzsODyvLU8FsiWOq5QdQcyYzeNwQRccLWzK5NZWNyqVJC1c8O0jKvnamo1sOCCpcFllgaXWtOH3WMEmb66IKuAC67msbDrMmzNnQJZivYtTzKZhGteUgUsrfyW0tom3FZQ9OOCTCZrVhgak2H/V5lPfOnYer8ALQgy2alBbvZWm8GvLgYuHWpe7EgBCuQ/mvGogOViTALPkJnMTodb4WTcjO3ZuNVJpQLmglxMCpMAFwSWl8Bz+LRFBsmXXR7HDbxqzGrg2lq54Olh63hr3TtuM3C9YNY6gQTWOXVHhQaWX3BY0PO+7FK5Soz6edittOLEsMAFjjdX+PV8qfDbJV+JCoscFliSBjypVBvI3ARV8JZBIYFL9m7p4HatkMCdow2cwZ0jytPaAmfK4PZod4hIYGf0BE5gZwHn5jODE9hZwLn5zOD/FPg07zefJoN5Q9w5GbuaDwksWGQwH01pB2WJ1bY5JC4kMCMTZN6C5KsA4AH+rHCMzweJ5bRZibEbFhgeQebLLHzoCOKzwjHgCQ9ZQgOHFDN2KoGNYNbwBLaKGeMT2AhmDU9gq5gxPoGNYNbwBLaKGeMT2AhmDU9gq5gxPoGNYNbwwcDWy58/PoGd73ECJ7CzgHPzmcEJ7Czg3HxmsDPwXwAAAP//cTdVEgAAAAZJREFUAwAzSpzAb2EzkQAAAABJRU5ErkJggg==";
}
