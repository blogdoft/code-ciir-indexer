namespace Ciir.Indexer.Api.Input;

/// <summary>
/// Server-side allow-list configuration for CIIR file paths (spec §42). A request supplies an
/// arbitrary path string, so this is the boundary that decides which parts of the filesystem the
/// API is even allowed to read from.
/// </summary>
public sealed class IndexerPathOptions
{
    /// <summary>The configuration section name this binds from (<c>appsettings.json</c>'s <c>Indexer</c> object).</summary>
    public const string SectionName = "Indexer";

    /// <summary>Gets or sets the absolute directories a request path must resolve inside of.</summary>
    /// <value>Defaults to an empty list, which rejects every path.</value>
    public IReadOnlyList<string> AllowedInputRoots { get; set; } = [];

    /// <summary>Gets or sets the required file extension, including the leading dot (e.g. ".jsonl").</summary>
    /// <value>Defaults to <c>".jsonl"</c>.</value>
    public string ExpectedExtension { get; set; } = ".jsonl";
}
