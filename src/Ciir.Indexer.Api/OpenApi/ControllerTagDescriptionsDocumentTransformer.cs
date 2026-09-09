using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ciir.Indexer.Api.OpenApi;

/// <summary>
/// Fills in each OpenAPI tag's description from the XML doc <c>&lt;summary&gt;</c> on the
/// controller class it was grouped by - the built-in OpenAPI generator groups actions into tags by
/// controller name automatically, but leaves each tag's own <c>description</c> empty.
/// </summary>
internal sealed class ControllerTagDescriptionsDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Assigns a description to each of <paramref name="document"/>'s tags, sourced from its controller's XML doc summary.</summary>
    /// <param name="document">The OpenAPI document being built.</param>
    /// <param name="context">Provides the API description groups used to map each tag back to its controller type.</param>
    /// <param name="cancellationToken">Unused - this transformer does no asynchronous work.</param>
    /// <returns>A completed task; this transformer mutates <paramref name="document"/> synchronously.</returns>
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var summariesByTypeName = XmlDocSummaries.LoadTypeSummaries();

        var controllerTypesByTag = context.DescriptionGroups
            .SelectMany(group => group.Items)
            .Select(description => description.ActionDescriptor as ControllerActionDescriptor)
            .Where(descriptor => descriptor is not null)
            .ToLookup(descriptor => descriptor!.ControllerName, descriptor => descriptor!.ControllerTypeInfo);

        foreach (var tag in document.Tags ?? Enumerable.Empty<OpenApiTag>())
        {
            if (tag.Name is not { } tagName)
            {
                continue;
            }

            var controllerType = controllerTypesByTag[tagName].FirstOrDefault();
            if (controllerType is not null && summariesByTypeName.TryGetValue(controllerType.FullName!, out var summary))
            {
                tag.Description = summary;
            }
        }

        return Task.CompletedTask;
    }
}
