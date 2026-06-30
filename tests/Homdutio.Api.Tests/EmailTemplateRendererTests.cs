using Homdutio.Api.Email;

namespace Homdutio.Api.Tests;

/// <summary>
/// Unit-tests the embedded-template renderer: both v1 templates load from the assembly, every placeholder
/// is substituted (none left behind), and interpolated values are HTML-encoded against markup injection.
/// </summary>
public class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    [Fact]
    public void Render_ResetTemplate_LoadsAndSubstitutesLink()
    {
        var html = _renderer.Render("reset-password", new Dictionary<string, string>
        {
            ["reset_link"] = "https://app.example/reset",
        });

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("https://app.example/reset", html);
        Assert.DoesNotContain("{{reset_link}}", html);
    }

    [Fact]
    public void Render_InviteTemplate_LoadsAndSubstitutesAllPlaceholders()
    {
        var html = _renderer.Render("invite-member", new Dictionary<string, string>
        {
            ["invite_link"] = "https://app.example/join/tok",
            ["household_name"] = "The Smiths",
            ["inviter_name"] = "Alex",
        });

        Assert.Contains("https://app.example/join/tok", html);
        Assert.Contains("The Smiths", html);
        Assert.Contains("Alex", html);
        Assert.DoesNotContain("{{invite_link}}", html);
        Assert.DoesNotContain("{{household_name}}", html);
        Assert.DoesNotContain("{{inviter_name}}", html);
    }

    [Fact]
    public void Render_HtmlEncodesInterpolatedValues()
    {
        var html = _renderer.Render("invite-member", new Dictionary<string, string>
        {
            ["invite_link"] = "https://app.example/join/tok",
            ["household_name"] = "<script>alert(1)</script>",
            ["inviter_name"] = "Alex & Sam",
        });

        // The raw markup must not survive; it is encoded.
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("Alex &amp; Sam", html);
    }

    [Fact]
    public void Render_UnknownTemplate_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => _renderer.Render("does-not-exist", new Dictionary<string, string>()));
    }
}
