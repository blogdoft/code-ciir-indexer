namespace Ciir.Indexer.Core;

/// <summary>
/// A CIIR document's symbol identity (spec's <c>symbol</c> object: name, qualifiedName,
/// canonicalName, container). <see cref="QualifiedName"/> excludes parameter types and can be
/// ambiguous across overloads; <see cref="CanonicalName"/> includes them and is unambiguous.
/// </summary>
public sealed record CiirSymbol
{
    public required string Name { get; init; }

    public required string QualifiedName { get; init; }

    public required string CanonicalName { get; init; }

    public string? Container { get; init; }
}
