using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Core;

/// <inheritdoc cref="IRelationIdentityKeyGenerator" />
public sealed class RelationIdentityKeyGenerator : IRelationIdentityKeyGenerator
{
    /// <summary>ASCII Unit Separator - not expected to appear in any legitimate CIIR field value.</summary>
    private const char FieldSeparator = '';

    public string Generate(long projectId, CiirRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);

        var canonical = string.Join(
            FieldSeparator,
            projectId.ToString(CultureInfo.InvariantCulture),
            relation.SourceCiirId.Value,
            relation.Kind,
            relation.TargetCiirId?.Value ?? string.Empty,
            relation.TargetSymbol,
            relation.SourcePath ?? string.Empty,
            FormatNullable(relation.StartLine),
            FormatNullable(relation.StartColumn),
            FormatNullable(relation.EndLine),
            FormatNullable(relation.EndColumn));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    private static string FormatNullable(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
