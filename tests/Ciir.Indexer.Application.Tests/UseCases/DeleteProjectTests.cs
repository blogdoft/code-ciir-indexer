using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class DeleteProjectTests
{
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();

    [Fact]
    public async Task ExecuteAsync_ExistingProject_ReturnsSuccess()
    {
        var existing = BuildProject();
        _projectStore.GetByPublicIdAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _projectStore.DeleteAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().ExecuteAsync(existing.PublicId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ReturnsProjectNotFound()
    {
        var missingId = Guid.NewGuid();
        _projectStore.GetByPublicIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().ExecuteAsync(missingId);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-project-not-found");
    }

    private static Project BuildProject() => Project.Create(
        "proj",
        gitUrl: null,
        gitRawUrl: null,
        EmbeddingModel.Create("bge-m3", 1024).Value,
        publicId: Guid.NewGuid(),
        id: 1).Value;

    private DeleteProject CreateSut() => new(_projectStore);
}
