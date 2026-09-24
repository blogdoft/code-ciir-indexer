using BlogDoFT.Libs.ResultPattern;
using FluentValidation;

namespace Ciir.Indexer.Core;

/// <summary>
/// The logical grouping of CIIR documents (spec §9/§11). <see cref="Name"/> is caller-supplied per
/// <c>POST /api/indexations</c> request, not derived from any CIIR record; <see
/// cref="EmbeddingModel"/> records which embedding configuration is currently authoritative for
/// this project's vectors.
/// </summary>
public sealed record Project
{
    public const int MaxNameLength = 200;

    private static readonly ProjectValidator Validator = new();

    private Project()
    {
    }

    public long Id { get; init; }

    public required string Name { get; init; }

    public string? GitUrl { get; init; }

    public string? GitRawUrl { get; init; }

    public required EmbeddingModel EmbeddingModel { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }

    /// <summary>Validates and creates a <see cref="Project"/>.</summary>
    /// <param name="name">The project's caller-supplied logical identity; must not be empty.</param>
    /// <param name="gitUrl">The project's git repository URL, if supplied by the caller.</param>
    /// <param name="gitRawUrl">The project's git raw-content URL, if supplied by the caller.</param>
    /// <param name="embeddingModel">The embedding model/dimensions currently configured for it.</param>
    /// <param name="id">The persisted id, or 0 for a project not yet persisted.</param>
    /// <param name="createdAt">The persisted creation timestamp, or null to stamp the current time.</param>
    /// <param name="updatedAt">The persisted last-update timestamp, or null to stamp the current time.</param>
    public static Result<Project> Create(
        string? name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        long id = 0,
        DateTime? createdAt = null,
        DateTime? updatedAt = null)
    {
        var now = DateTime.UtcNow;
        var candidate = new Project
        {
            Id = id,
            Name = name ?? string.Empty,
            GitUrl = gitUrl,
            GitRawUrl = gitRawUrl,
            EmbeddingModel = embeddingModel,
            CreatedAt = createdAt ?? now,
            UpdatedAt = updatedAt ?? now,
        };

        var validation = Validator.Validate(candidate);
        if (!validation.IsValid)
        {
            return Result<Project>.FromFailure(new Failure("400-project-invalid", validation.Errors[0].ErrorMessage));
        }

        return Result<Project>.FromSuccess(candidate);
    }

    private sealed class ProjectValidator : AbstractValidator<Project>
    {
        public ProjectValidator()
        {
            RuleFor(project => project.Name).NotEmpty().MaximumLength(MaxNameLength);
            RuleFor(project => project.EmbeddingModel).NotNull();
        }
    }
}
