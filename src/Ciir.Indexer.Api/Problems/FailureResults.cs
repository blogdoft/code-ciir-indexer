using BlogDoFT.Libs.ResultPattern;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Ciir.Indexer.Api.Problems;

/// <summary>
/// Translates a domain <see cref="Failure"/> into an HTTP result. Application use cases encode the
/// intended HTTP status as the leading digits of <see cref="Failure.Code"/> (e.g.
/// "400-path-required", "404-path-not-found"), so this stays a single, generic mapping instead of
/// a per-endpoint switch statement.
/// </summary>
public static class FailureResults
{
    /// <summary>Maps a domain <see cref="Failure"/> to the HTTP response it represents.</summary>
    /// <param name="failure">The failure to translate, whose <c>Code</c> encodes the target HTTP status.</param>
    /// <param name="context">The current request's <see cref="HttpContext"/>, used for the Problem Details <c>instance</c> field.</param>
    /// <returns>
    /// A body-less 404 for a <c>"404-..."</c> code; otherwise an RFC 7807
    /// <c>application/problem+json</c> result at the encoded status.
    /// </returns>
    public static IActionResult ToActionResult(this Failure failure, HttpContext context)
    {
        var status = ParseStatus(failure.Code);

        if (status == StatusCodes.Status404NotFound)
        {
            return new NotFoundResult();
        }

        return ProblemResults.Build(status, ReasonPhrases.GetReasonPhrase(status), failure.Message, context.Request.Path);
    }

    private static int ParseStatus(string code)
    {
        var separatorIndex = code.IndexOf('-', StringComparison.Ordinal);
        var statusPart = separatorIndex > 0 ? code[..separatorIndex] : code;
        return int.TryParse(statusPart, out var status) ? status : StatusCodes.Status400BadRequest;
    }
}
