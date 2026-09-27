using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>Field constraints shared by <see cref="ListProjects"/>, <see cref="CreateProject"/> and <see cref="UpdateProject"/>.</summary>
public static class ProjectValidation
{
    public const int MaxNameFilterLength = 200;
    public const int MaxNameLength = 200;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Validates the name field shared by create and update.</summary>
    /// <param name="name">The project's name, as supplied by the caller.</param>
    /// <returns>The violated <see cref="Failure"/>, or null when the name is valid.</returns>
    public static Failure? ValidateFields(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ProjectFailures.NameRequired();
        }

        if (name.Length > MaxNameLength)
        {
            return ProjectFailures.NameTooLong(MaxNameLength);
        }

        return null;
    }
}
