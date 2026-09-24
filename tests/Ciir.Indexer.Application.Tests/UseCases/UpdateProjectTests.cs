using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class UpdateProjectTests
{
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();

    [Fact]
    public async Task ExecuteAsync_ExistingProject_UpdatesAndReturnsIt()
    {
        var existing = BuildProject();
        var updated = existing with { Name = "renamed" };
        _projectStore.GetByPublicIdAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _projectStore.ExistsByNameAsync("renamed", existing.Id, Arg.Any<CancellationToken>()).Returns(false);
        _projectStore
            .UpdateAsync(existing.Id, "renamed", null, null, Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(updated);

        var result = await CreateSut().ExecuteAsync(existing.PublicId, "renamed", "bge-m3", 1024, null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(updated);
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ReturnsProjectNotFound()
    {
        var missingId = Guid.NewGuid();
        _projectStore.GetByPublicIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().ExecuteAsync(missingId, "proj", "bge-m3", 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-project-not-found");
    }

    [Fact]
    public async Task ExecuteAsync_NameUsedByAnotherProject_ReturnsNameConflict()
    {
        var existing = BuildProject();
        _projectStore.GetByPublicIdAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _projectStore.ExistsByNameAsync("taken", existing.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().ExecuteAsync(existing.PublicId, "taken", "bge-m3", 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("409-name-conflict");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteAsync_MissingName_ReturnsNameRequired(string? name)
    {
        var result = await CreateSut().ExecuteAsync(Guid.NewGuid(), name, "bge-m3", 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-required");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static Project BuildProject() => Project.Create(
        "proj",
        gitUrl: null,
        gitRawUrl: null,
        EmbeddingModel.Create("bge-m3", 1024).Value,
        publicId: Guid.NewGuid(),
        id: 1).Value;

    private UpdateProject CreateSut() => new(_projectStore);
}
