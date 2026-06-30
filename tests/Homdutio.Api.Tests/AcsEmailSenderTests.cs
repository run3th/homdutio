using System.Net;
using Homdutio.Api.Email;

namespace Homdutio.Api.Tests;

/// <summary>
/// Unit-tests the pure transactional-email composition (no live ACS client): each built message targets
/// the requested recipient, sends from the configured address, carries the raw link in the plain-text
/// body, and renders the embedded HTML template with the link/values HTML-encoded (defense-in-depth
/// against markup injection). Uses a real <see cref="EmailTemplateRenderer"/> reading the embedded bodies.
/// </summary>
public class AcsEmailSenderTests
{
    private static readonly AcsEmailOptions Options = new()
    {
        SenderAddress = "DoNotReply@homdutio.example",
    };

    private static readonly EmailTemplateRenderer Renderer = new();

    [Fact]
    public void BuildResetMessage_TargetsRecipientAndContainsLink()
    {
        const string recipient = "user@example.com";
        const string resetLink = "https://app.example/reset-password?email=user%40example.com&token=abc123";

        var message = AcsEmailSender.BuildResetMessage(Options, Renderer, recipient, resetLink);

        Assert.Equal(Options.SenderAddress, message.SenderAddress);
        Assert.Equal(recipient, Assert.Single(message.Recipients.To).Address);

        // Plain text carries the raw link; the rendered HTML carries the HTML-encoded link (the `&` → `&amp;`).
        Assert.Contains(resetLink, message.Content.PlainText);
        Assert.Contains(WebUtility.HtmlEncode(resetLink), message.Content.Html);
        // The template fully rendered — no leftover placeholder.
        Assert.DoesNotContain("{{reset_link}}", message.Content.Html);
    }

    [Fact]
    public void BuildInviteMessage_TargetsRecipientAndSubstitutesAllValues()
    {
        const string recipient = "joiner@example.com";
        const string inviteLink = "https://app.example/join/abc123token";
        const string householdName = "The Smiths";
        const string inviterName = "Alex";

        var message = AcsEmailSender.BuildInviteMessage(Options, Renderer, recipient, inviteLink, householdName, inviterName);

        Assert.Equal(Options.SenderAddress, message.SenderAddress);
        Assert.Equal(recipient, Assert.Single(message.Recipients.To).Address);
        Assert.Contains(householdName, message.Content.Subject);

        // Plain text carries the raw link; rendered HTML carries the encoded link + household + inviter.
        Assert.Contains(inviteLink, message.Content.PlainText);
        Assert.Contains(WebUtility.HtmlEncode(inviteLink), message.Content.Html);
        Assert.Contains(householdName, message.Content.Html);
        Assert.Contains(inviterName, message.Content.Html);
        Assert.DoesNotContain("{{invite_link}}", message.Content.Html);
        Assert.DoesNotContain("{{household_name}}", message.Content.Html);
        Assert.DoesNotContain("{{inviter_name}}", message.Content.Html);
    }
}
