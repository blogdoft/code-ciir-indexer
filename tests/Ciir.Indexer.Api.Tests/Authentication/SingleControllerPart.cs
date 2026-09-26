using Microsoft.AspNetCore.Mvc.ApplicationParts;
using System.Reflection;

namespace Ciir.Indexer.Api.Tests.Authentication;

/// <summary>An MVC application part that exposes exactly one controller type, so a test host maps only that controller's routes.</summary>
internal sealed class SingleControllerPart(Type controllerType) : ApplicationPart, IApplicationPartTypeProvider
{
    public override string Name => $"{nameof(SingleControllerPart)}({controllerType.Name})";

    public IEnumerable<TypeInfo> Types { get; } = [controllerType.GetTypeInfo()];
}
