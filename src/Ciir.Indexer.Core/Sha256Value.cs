using System.Text.RegularExpressions;

namespace Ciir.Indexer.Core;

/// <summary>
/// Shared validation for the "sha256:&lt;64 hex chars&gt;" string format used by both
/// <see cref="CiirIdentity"/> and <see cref="EmbeddingTextHash"/> - two distinct concepts that
/// happen to share a wire format but must never be treated as interchangeable.
/// </summary>
internal static partial class Sha256Value
{
    private const string Prefix = "sha256:";

    public static string Validate(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", paramName);
        }

        if (!value.StartsWith(Prefix, StringComparison.Ordinal) || !HexPattern().IsMatch(value.AsSpan(Prefix.Length)))
        {
            throw new ArgumentException($"Value must match 'sha256:<64 hex chars>', got '{value}'.", paramName);
        }

        return value;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexPattern();
}
