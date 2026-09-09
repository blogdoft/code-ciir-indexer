using System.Text;
using System.Xml.Linq;

namespace Ciir.Indexer.Api.OpenApi;

/// <summary>
/// Reads the type-level (<c>&lt;summary&gt;</c>) documentation this project's own build emits to
/// <c>Ciir.Indexer.Api.xml</c> (via <c>GenerateDocumentationFile</c>), keyed by full type name -
/// used to fill in OpenAPI document/tag descriptions that the built-in generator doesn't source
/// from XML comments on its own (e.g. <see cref="Microsoft.OpenApi.OpenApiTag.Description"/>).
/// </summary>
internal static class XmlDocSummaries
{
    /// <summary>Loads every type's <c>&lt;summary&gt;</c> text from this assembly's generated XML documentation file.</summary>
    /// <returns>A map from full type name (e.g. <c>Ciir.Indexer.Api.Controllers.IndexationsController</c>) to its trimmed summary text.</returns>
    public static IReadOnlyDictionary<string, string> LoadTypeSummaries()
    {
        var xmlDocPath = Path.Combine(AppContext.BaseDirectory, "Ciir.Indexer.Api.xml");
        if (!File.Exists(xmlDocPath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return XDocument.Load(xmlDocPath)
            .Descendants("member")
            .Where(member => ((string?)member.Attribute("name"))?.StartsWith("T:", StringComparison.Ordinal) == true)
            .ToDictionary(
                member => ((string)member.Attribute("name")!)[2..],
                member => member.Element("summary") is { } summary ? RenderSummary(summary) : string.Empty,
                StringComparer.Ordinal);
    }

    // Renders a <summary> element's mixed text/markup content as plain-ish text suitable for an
    // OpenAPI description: <see cref="..."/> becomes the referenced member's short name (raw
    // XDocument.Value would silently drop it), <c>...</c> becomes `...` (matching how the built-in
    // OpenAPI generator itself renders inline code from XML docs), and the doc comment's original
    // indentation/line breaks collapse into single spaces.
    private static string RenderSummary(XElement summary)
    {
        var builder = new StringBuilder();
        foreach (var node in summary.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement { Name.LocalName: "see" } seeElement:
                    builder.Append(RenderCref((string?)seeElement.Attribute("cref")));
                    break;
                case XElement { Name.LocalName: "c" } codeElement:
                    builder.Append('`').Append(codeElement.Value).Append('`');
                    break;
                case XElement otherElement:
                    builder.Append(otherElement.Value);
                    break;
            }
        }

        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RenderCref(string? cref)
    {
        if (string.IsNullOrEmpty(cref))
        {
            return string.Empty;
        }

        var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }
}
