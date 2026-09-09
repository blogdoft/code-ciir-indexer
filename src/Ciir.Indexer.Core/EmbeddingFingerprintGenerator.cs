using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Core;

/// <inheritdoc cref="IEmbeddingFingerprintGenerator" />
public sealed class EmbeddingFingerprintGenerator : IEmbeddingFingerprintGenerator
{
    public string Generate(string embeddingTextHash, string model, int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingTextHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{embeddingTextHash}\n{model}\n{dimensions}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }
}
