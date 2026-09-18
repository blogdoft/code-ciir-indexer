using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ciir.Indexer.Api.OpenApi;

/// <summary>
/// Sets the OpenAPI document's server URL to wherever this instance is actually reachable, so
/// Swagger UI's "Try it out" requests land on a real address whether the app is running behind the
/// cluster's blogdoft.home.arpa/code-brain ingress prefix (which strips "/code-brain" before
/// forwarding - see .eng/k8s/middleware.yaml - leaving nothing in the request itself that reveals
/// it) or directly via `dotnet run`. The "PublicBaseUrl" configuration value supplies that external
/// prefix explicitly for the one deployment that needs it; everywhere else, the current request's
/// own scheme/host stand in for it.
/// </summary>
internal sealed class PublicServerDocumentTransformer(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    : IOpenApiDocumentTransformer
{
    /// <summary>Sets <paramref name="document"/>'s server list to a single, resolvable base URL.</summary>
    /// <param name="document">The OpenAPI document being built.</param>
    /// <param name="context">Unused - the base URL comes from configuration/the current request, not the description groups.</param>
    /// <param name="cancellationToken">Unused - this transformer does no asynchronous work.</param>
    /// <returns>A completed task; this transformer mutates <paramref name="document"/> synchronously.</returns>
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = configuration["PublicBaseUrl"] ?? (request is null ? null : $"{request.Scheme}://{request.Host}");

        if (baseUrl is not null)
        {
            document.Servers = [new OpenApiServer { Url = baseUrl }];
        }

        return Task.CompletedTask;
    }
}
