using System.Net;
using Homdutio.Api.Email;

namespace Homdutio.Api.Tests;

/// <summary>
/// Unit-tests the pure reset-email composition (no live ACS client): the built message must target
/// the requested recipient, send from the configured address, carry the raw link in the plain-text
/// body, and carry the HTML-encoded link in the HTML body (defense-in-depth against markup injection).
/// </summary>
public class AcsEmailSenderTests
{
    private static readonly AcsEmailOptions Options = new()
    {
        SenderAddress = "DoNotReply@homdutio.example",
    };

    [Fact]
    public void BuildResetMessage_TargetsRecipientAndContainsLink()
    {
        const string recipient = "user@example.com";
        const string resetLink = "https://app.example/reset-password?email=user%40example.com&token=abc123";

        var message = AcsEmailSender.BuildResetMessage(Options, recipient, resetLink);

        Assert.Equal(Options.SenderAddress, message.SenderAddress);
        Assert.Equal(recipient, Assert.Single(message.Recipients.To).Address);

        // Plain text carries the raw link; HTML carries the HTML-encoded link (the `&` becomes `&amp;`).
        Assert.Contains(resetLink, message.Content.PlainText);
        Assert.Contains(WebUtility.HtmlEncode(resetLink), message.Content.Html);
    }
}
