using Microsoft.AspNetCore.Mvc;

namespace Ciir.Indexer.Api.Problems;

/// <summary>Builds RFC 7807 "application/problem+json" results.</summary>
public static class ProblemResults
{
    /// <summary>Builds an RFC 7807 <c>application/problem+json</c> result.</summary>
    /// <param name="status">The HTTP status code, used for both the response status and the Problem Details <c>status</c> field.</param>
    /// <param name="title">A short, human-readable summary of the problem type (e.g. the standard reason phrase for <paramref name="status"/>).</param>
    /// <param name="detail">A human-readable explanation specific to this occurrence of the problem.</param>
    /// <param name="instance">The request path that triggered the problem, echoed back as the Problem Details <c>instance</c> field.</param>
    /// <returns>An <see cref="ObjectResult"/> carrying the Problem Details body at the given status, with content type <c>application/problem+json</c>.</returns>
    public static IActionResult Build(int status, string title, string detail, PathString instance)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{status}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = instance,
        };

        return new ObjectResult(problemDetails)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
