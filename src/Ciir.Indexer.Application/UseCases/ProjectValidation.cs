using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>Field constraints shared by <see cref="ListProjects"/>, <see cref="CreateProject"/> and <see cref="UpdateProject"/>.</summary>
public static class ProjectValidation
{
    public const int MaxNameFilterLength = 200;
    public const int MaxNameLength = 200;
    public const int MaxEmbeddingModelLength = 200;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Validates the name/embeddingModel/embeddingDimensions fields shared by create and update.</summary>
    /// <param name="name">The project's name, as supplied by the caller.</param>
    /// <param name="embeddingModel">The project's embedding model name, as supplied by the caller.</param>
    /// <param name="embeddingDimensions">The project's embedding dimensionality, as supplied by the caller.</param>
    /// <returns>The first violated <see cref="Failure"/>, or null when every field is valid.</returns>
    public static Failure? ValidateFields(string? name, string? embeddingModel, int? embeddingDimensions)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ProjectFailures.NameRequired();
        }

        if (name.Length > MaxNameLength)
        {
            return ProjectFailures.NameTooLong(MaxNameLength);
        }

        if (string.IsNullOrWhiteSpace(embeddingModel))
        {
            return ProjectFailures.EmbeddingModelRequired();
        }

        if (embeddingModel.Length > MaxEmbeddingModelLength)
        {
            return ProjectFailures.EmbeddingModelTooLong(MaxEmbeddingModelLength);
        }

        if (embeddingDimensions is null)
        {
            return ProjectFailures.EmbeddingDimensionsRequired();
        }

        if (embeddingDimensions <= 0)
        {
            return ProjectFailures.EmbeddingDimensionsInvalid();
        }

        return null;
    }
}
