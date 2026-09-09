namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>The embedding configuration is invalid or incomplete for the selected provider.</summary>
public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(string message)
        : base(message)
    {
    }

    public ConfigurationValidationException()
    {
    }

    public ConfigurationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
