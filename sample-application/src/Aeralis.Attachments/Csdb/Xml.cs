using System.Text.RegularExpressions;
using System.Xml;

namespace Aeralis.Attachments.Csdb;

/// <summary>
/// A very small element-building helper. The projection writes a few thousand elements per
/// run and reads better as nested calls than as pages of <c>CreateElement</c> and
/// <c>AppendChild</c>.
/// </summary>
internal static partial class Xml
{
    /// <summary>An element with attributes given as <c>name, value</c> pairs and no children.</summary>
    public static XmlElement El(XmlDocument doc, string name, params string?[] attributes)
    {
        var element = doc.CreateElement(name);
        for (var i = 0; i + 1 < attributes.Length; i += 2)
        {
            if (attributes[i] is { } attributeName && attributes[i + 1] is { } value)
            {
                element.SetAttribute(attributeName, value);
            }
        }
        return element;
    }

    /// <summary>An element holding text.</summary>
    public static XmlElement Text(XmlDocument doc, string name, string? text)
    {
        var element = doc.CreateElement(name);
        element.AppendChild(doc.CreateTextNode(Clean(text)));
        return element;
    }

    /// <summary>Appends children and returns the parent, so calls can be nested.</summary>
    public static XmlElement With(this XmlElement parent, params XmlNode?[] children)
    {
        foreach (var child in children)
        {
            if (child is not null) parent.AppendChild(child);
        }
        return parent;
    }

    /// <summary>Appends a sequence of children and returns the parent.</summary>
    public static XmlElement WithAll(this XmlElement parent, IEnumerable<XmlNode?> children) =>
        parent.With(children.ToArray());

    /// <summary>
    /// Text as it can go into an XML document: control characters out, runs of whitespace
    /// collapsed. The dataset's prose is single-line already, but a stray newline in a
    /// section would otherwise arrive in the PDF as a gap in the middle of a sentence.
    /// </summary>
    public static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : Whitespace().Replace(text.Trim(), " ");

    /// <summary>
    /// Splits prose into sentences, keeping the terminator. Used where a schema wants a list
    /// of short statements — preliminary requirements, close-out conditions — and the dataset
    /// holds one paragraph.
    /// </summary>
    /// <remarks>
    /// Deliberately simple. The split is on a full stop followed by a space and a capital,
    /// which leaves "6.0 man-hours" and "MOD-168276." intact; an abbreviation such as "approx."
    /// mid-sentence would split wrongly, and the dataset's register does not use them.
    /// </remarks>
    public static List<string> Sentences(string? text)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return [];

        return SentenceBreak().Split(clean)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Splits a numbered procedure — "1. Do this. 2. Do that." — into its steps, dropping the
    /// numbers, which the presentation stylesheet re-generates in ATA style.
    /// </summary>
    public static List<string> NumberedSteps(string? text)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return [];

        var parts = StepBreak().Split(clean)
            .Select(s => StepNumber().Replace(s.Trim(), "").Trim())
            .Where(s => s.Length > 0)
            .ToList();

        // Not a numbered procedure at all: fall back to sentences so the caller always gets
        // something step-shaped rather than one enormous step.
        return parts.Count > 1 ? parts : Sentences(clean);
    }

    /// <summary>Turns a section key such as <c>job_setup_note</c> into "Job set-up note".</summary>
    public static string Humanise(string key)
    {
        var words = key.Replace('_', ' ').Trim();
        if (words.Length == 0) return words;

        // A handful of terms that the naive rule gets wrong or that read better spelled out.
        words = words switch
        {
            "job setup note" => "job set-up information",
            "close out" => "close-out requirements",
            "ata" => "ATA",
            _ => words,
        };

        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z(])")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"(?<=[.;])\s+(?=\d{1,2}\.\s)")]
    private static partial Regex StepBreak();

    [GeneratedRegex(@"^\d{1,2}\.\s*")]
    private static partial Regex StepNumber();
}
