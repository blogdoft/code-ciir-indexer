using Microsoft.AspNetCore.Mvc;

namespace Ciir.Indexer.Api.Problems;

/// <summary>Parses a route parameter that must be a positive 64-bit integer id (e.g. <c>projectId</c>).</summary>
public static class RouteId
{
    /// <summary>Attempts to parse <paramref name="value"/> as a positive 64-bit integer id.</summary>
    /// <param name="value">The raw route parameter value.</param>
    /// <param name="parameterName">The route parameter's name, echoed back in the failure message.</param>
    /// <param name="requestPath">The current request path, echoed back as the Problem Details <c>instance</c> field.</param>
    /// <param name="id">The parsed id, when parsing succeeds.</param>
    /// <param name="problem">A 400 Problem Details result, when parsing fails.</param>
    /// <returns><c>true</c> when <paramref name="value"/> is a positive integer; <c>false</c> otherwise.</returns>
    public static bool TryParsePositive(
        string value,
        string parameterName,
        PathString requestPath,
        out long id,
        out IActionResult? problem)
    {
        if (long.TryParse(value, out id) && id >= 1)
        {
            problem = null;
            return true;
        }

        problem = ProblemResults.Build(
            StatusCodes.Status400BadRequest,
            "Bad Request",
            $"The '{parameterName}' route parameter must be a positive integer; received '{value}'.",
            requestPath);
        return false;
    }
}
