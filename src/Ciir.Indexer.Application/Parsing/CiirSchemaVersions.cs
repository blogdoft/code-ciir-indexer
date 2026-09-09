namespace Ciir.Indexer.Application.Parsing;

/// <summary>
/// The CIIR <c>schemaVersion</c> major versions this indexer accepts (spec §44). Compatibility is
/// checked by major version only, not by exact "major.minor" match: a minor version bump (e.g.
/// "1.0" -&gt; "1.1") is, per semver and spec §13's own "CIIR v1.0 / v1.1 / v1.x" framing, a
/// backward-compatible addition of optional fields - never a breaking change requiring an indexer
/// update - so it is accepted without needing this list to grow on every generator release. An
/// unrecognized MAJOR version is never silently interpreted - it always surfaces as a per-record
/// parse error.
/// </summary>
public static class CiirSchemaVersions
{
    public static readonly IReadOnlyCollection<string> SupportedMajorVersions = ["1"];

    public static bool IsSupported(string schemaVersion) =>
        SupportedMajorVersions.Contains(MajorVersion(schemaVersion), StringComparer.Ordinal);

    private static string MajorVersion(string schemaVersion) => schemaVersion.Split('.', 2)[0];
}
