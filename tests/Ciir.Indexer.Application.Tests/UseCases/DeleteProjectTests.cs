using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class DeleteProjectTests
{
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();

    [Fact]
    public async Task ExecuteAsync_ExistingProject_ReturnsSuccess()
    {
        _projectStore.DeleteAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().ExecuteAsync(1);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ReturnsProjectNotFound()
    {
        _projectStore.DeleteAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut().ExecuteAsync(999);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-project-not-found");
    }

    private DeleteProject CreateSut() => new(_projectStore);
}
