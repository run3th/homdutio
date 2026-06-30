using System.Collections.Concurrent;
using System.Net;
using System.Reflection;

namespace Homdutio.Api.Email;

/// <summary>
/// Loads an embedded HTML email template by logical name (e.g. <c>"reset-password"</c>) and substitutes
/// its <c>{{snake_case}}</c> placeholders. Every interpolated value is HTML-encoded before substitution —
/// defense-in-depth against markup injection through a display name or a link, orthogonal to any encoding
/// the caller already applied to a URL. Templates ship as <c>&lt;EmbeddedResource&gt;</c>s under
/// <c>Email/Templates/</c> so they are versioned with the assembly; the loaded text is cached per name.
/// Registered as a singleton in DI.
/// </summary>
public sealed class EmailTemplateRenderer
{
    private static readonly Assembly ResourceAssembly = typeof(EmailTemplateRenderer).Assembly;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    /// <summary>
    /// Renders <paramref name="templateName"/> (without the <c>.html</c> suffix), replacing each
    /// <c>{{key}}</c> with the HTML-encoded value from <paramref name="values"/>. Placeholders with no
    /// matching key are left untouched. Throws when the template is not embedded in the assembly.
    /// </summary>
    public string Render(string templateName, IReadOnlyDictionary<string, string> values)
    {
        var rendered = _cache.GetOrAdd(templateName, Load);

        foreach (var (key, value) in values)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", WebUtility.HtmlEncode(value));
        }

        return rendered;
    }

    /// <summary>
    /// Reads the embedded template text. The build mangles directory separators into dots, so the logical
    /// name ends with <c>.Templates.&lt;name&gt;.html</c> — matched by suffix to stay robust to the root
    /// namespace rather than hard-coding the full resource name.
    /// </summary>
    private static string Load(string templateName)
    {
        var suffix = $".Templates.{templateName}.html";
        var resourceName = ResourceAssembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded email template '{templateName}' was not found.");

        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
