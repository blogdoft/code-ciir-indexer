using BlogDoFT.Libs.ResultPattern;
using FluentValidation;

namespace Ciir.Indexer.Core;

/// <summary>
/// The identity of an embedding model configuration: a vector can only be interpreted correctly
/// together with the model (and dimensionality) that produced it - embeddings from different
/// models must never be compared as if they belonged to the same vector space.
/// </summary>
public sealed record EmbeddingModel
{
    private static readonly EmbeddingModelValidator Validator = new();

    private EmbeddingModel(string name, int dimensions)
    {
        Name = name;
        Dimensions = dimensions;
    }

    public string Name { get; }

    public int Dimensions { get; }

    /// <summary>Validates and creates an <see cref="EmbeddingModel"/>.</summary>
    /// <param name="name">The embedding model's name; must not be empty.</param>
    /// <param name="dimensions">The embedding model's vector width; must be positive.</param>
    public static Result<EmbeddingModel> Create(string? name, int dimensions)
    {
        var candidate = new EmbeddingModel(name ?? string.Empty, dimensions);
        var validation = Validator.Validate(candidate);
        if (!validation.IsValid)
        {
            return Result<EmbeddingModel>.FromFailure(new Failure(
                "400-embedding-model-invalid", validation.Errors[0].ErrorMessage));
        }

        return Result<EmbeddingModel>.FromSuccess(candidate);
    }

    private sealed class EmbeddingModelValidator : AbstractValidator<EmbeddingModel>
    {
        public EmbeddingModelValidator()
        {
            RuleFor(model => model.Name).NotEmpty().WithMessage("Embedding model name must not be empty.");
            RuleFor(model => model.Dimensions).GreaterThan(0).WithMessage("Embedding dimensions must be positive.");
        }
    }
}
