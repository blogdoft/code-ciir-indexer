using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Ciir.Indexer.Application.Tests;

internal static class SubstituteScopeFactory
{
    public static IServiceScopeFactory For(params (Type ServiceType, object Instance)[] services)
    {
        var provider = Substitute.For<IServiceProvider>();
        foreach (var (serviceType, instance) in services)
        {
            provider.GetService(serviceType).Returns(instance);
        }

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        return factory;
    }
}
