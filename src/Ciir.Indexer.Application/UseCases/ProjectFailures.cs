using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Domain failures for the projects CRUD use cases. <see cref="Failure.Code"/> is prefixed with
/// the intended HTTP status, translated by <c>Ciir.Indexer.Api.Problems.FailureResults</c>.
/// </summary>
public static class ProjectFailures
{
    public static Failure NameFilterEmpty() => new(
        "400-name-filter-empty",
        "The 'name' query parameter must not be empty when provided.");

    public static Failure NameFilterTooLong(int maxLength) => new(
        "400-name-filter-too-long",
        $"The 'name' query parameter must not exceed {maxLength} characters.");

    public static Failure PageInvalid() => new(
        "400-page-invalid",
        "The 'page' query parameter must be zero or a positive integer.");

    public static Failure PageSizeInvalid(int maxPageSize) => new(
        "400-page-size-invalid",
        $"The 'page_size' query parameter must be between 1 and {maxPageSize}.");

    public static Failure ProjectNotFound(Guid projectId) => new(
        "404-project-not-found",
        $"No project exists with id {projectId}.");

    public static Failure NameRequired() => new(
        "400-name-required",
        "The 'name' field is required and must not be empty or blank.");

    public static Failure NameTooLong(int maxLength) => new(
        "400-name-too-long",
        $"The 'name' field must not exceed {maxLength} characters.");

    public static Failure NameConflict(string name) => new(
        "409-name-conflict",
        $"A project named '{name}' already exists.");

    public static Failure EmbeddingModelRequired() => new(
        "400-embedding-model-required",
        "The 'embeddingModel' field is required and must not be empty or blank.");

    public static Failure EmbeddingModelTooLong(int maxLength) => new(
        "400-embedding-model-too-long",
        $"The 'embeddingModel' field must not exceed {maxLength} characters.");

    public static Failure EmbeddingDimensionsRequired() => new(
        "400-embedding-dimensions-required",
        "The 'embeddingDimensions' field is required.");

    public static Failure EmbeddingDimensionsInvalid() => new(
        "400-embedding-dimensions-invalid",
        "The 'embeddingDimensions' field must be a positive integer.");
}
