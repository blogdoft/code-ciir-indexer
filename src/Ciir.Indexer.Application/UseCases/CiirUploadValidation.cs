using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>Field validation shared by <see cref="SubmitCiirUpload"/> and <see cref="RegisterCiirUpload"/>.</summary>
internal static class CiirUploadValidation
{
    /// <summary>Parses a raw <c>projectId</c> field value.</summary>
    /// <param name="projectId">The raw, untrusted <c>projectId</c> field value.</param>
    /// <param name="parsedProjectId">The parsed id, when parsing succeeds.</param>
    /// <param name="failure">A <c>400-project-id-required</c> failure, when parsing fails.</param>
    /// <returns><c>true</c> when <paramref name="projectId"/> parses as a valid id; <c>false</c> otherwise.</returns>
    public static bool TryParseProjectId(string? projectId, out Guid parsedProjectId, out Failure? failure)
    {
        if (string.IsNullOrWhiteSpace(projectId) || !Guid.TryParse(projectId, out parsedProjectId))
        {
            parsedProjectId = Guid.Empty;
            failure = new Failure("400-project-id-required", "The 'projectId' field is required and must be a valid project id.");
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Validates that <paramref name="fileName"/> ends in <c>.jsonl</c>.</summary>
    /// <param name="fileName">The file name (or object key) to validate.</param>
    /// <returns>A <c>400-invalid-extension</c> failure when the extension is wrong; <c>null</c> otherwise.</returns>
    public static Failure? ValidateJsonlExtension(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".jsonl", StringComparison.OrdinalIgnoreCase)
            ? null
            : new Failure("400-invalid-extension", "Expected a '.jsonl' file.");
}
