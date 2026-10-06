using System.Net;
using System.Text.RegularExpressions;

namespace AviationIntel.Connector.Extraction;

/// <summary>Turns the HTML fragments feeds carry in &lt;description&gt; and &lt;content:encoded&gt; into plain text.</summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<(script|style|noscript)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NonContent();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6]|/tr|/blockquote)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex BlankLines();

    //WordPress feeds end every summary with "The post X appeared first on Y." - boilerplate, not content.
    [GeneratedRegex(@"The post .{1,300}? appeared first on .{1,200}?\.?\s*$", RegexOptions.Singleline)]
    private static partial Regex AppearedFirstOn();

    public static string ToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var text = NonContent().Replace(html, " ");
        text = BlockBreaks().Replace(text, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = string.Join('\n', text.Split('\n').Select(l => l.Trim()));
        text = BlankLines().Replace(text, "\n\n");
        text = AppearedFirstOn().Replace(text, "");

        return text.Trim();
    }

    public static int WordCount(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
