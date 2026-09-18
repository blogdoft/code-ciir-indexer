using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ciir.Indexer.Api.Uploads;

/// <summary>
/// Removes MVC's built-in form-reading value provider factories for the action it decorates.
/// Without this, <c>[ApiController]</c> eagerly calls <c>Request.ReadFormAsync()</c> to build its
/// value providers before the action runs - even though this action binds nothing from the form -
/// which fully buffers/consumes the multipart body ahead of time. That is exactly what a streamed,
/// up-to-200 MB upload (upload spec §5/§12) cannot afford, and it leaves nothing for this action's
/// own <see cref="Microsoft.AspNetCore.WebUtilities.MultipartReader"/> to read.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    /// <summary>Removes the form-related value provider factories before model binding runs.</summary>
    /// <param name="context">The current resource-executing context.</param>
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    /// <summary>No-op; this filter only needs to act before model binding.</summary>
    /// <param name="context">The current resource-executed context.</param>
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
