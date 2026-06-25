using Homdutio.Api.Email;

namespace Homdutio.Api.Tests;

/// <summary>
/// Unit-tests the pure reset-email composition (no live ACS client): the built message must target
/// the requested recipient, send from the configured address, and carry the pre-built reset link in
/// both the plain-text and HTML body.
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

        Assert.Contains(resetLink, message.Content.PlainText);
        Assert.Contains(resetLink, message.Content.Html);
    }
}
