using Ciir.Indexer.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ciir.Indexer.Api.OpenApi;

/// <summary>
/// Documents the <c>multipart/form-data</c> request body of <see cref="CiirUploadsController.UploadAsync"/>,
/// which the built-in OpenAPI generator cannot infer on its own: that action reads the multipart
/// body by hand via <c>MultipartReader</c> instead of declaring a bound <c>[FromForm]</c>/<c>IFormFile</c>
/// parameter, so ApiExplorer sees no parameters to describe. Without this transformer, Swagger UI's
/// "Try it out" offers no way to attach the <c>projectId</c>/<c>ciirFile</c> fields for this operation.
/// </summary>
internal sealed class CiirUploadRequestBodyOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>Adds a request body schema to <paramref name="operation"/> when it describes <see cref="CiirUploadsController.UploadAsync"/>.</summary>
    /// <param name="operation">The OpenAPI operation being built.</param>
    /// <param name="context">Identifies which action <paramref name="operation"/> was generated from.</param>
    /// <param name="cancellationToken">Unused - this transformer does no asynchronous work.</param>
    /// <returns>A completed task; this transformer mutates <paramref name="operation"/> synchronously.</returns>
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor descriptor
            || descriptor.ControllerTypeInfo != typeof(CiirUploadsController)
            || descriptor.MethodInfo.Name != nameof(CiirUploadsController.UploadAsync))
        {
            return Task.CompletedTask;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { "projectId", "ciirFile" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["projectId"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Description = "An already-registered project's id. Must be sent before 'ciirFile' - it is validated before any byte of the file is stored.",
                            },
                            ["ciirFile"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "binary",
                                Description = "The CIIR '.jsonl' file to index.",
                            },
                        },
                    },
                },
            },
        };

        return Task.CompletedTask;
    }
}
