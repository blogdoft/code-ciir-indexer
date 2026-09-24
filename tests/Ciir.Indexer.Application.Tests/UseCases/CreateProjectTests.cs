using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class CreateProjectTests
{
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();

    [Fact]
    public async Task ExecuteAsync_ValidFields_InsertsAndReturnsProject()
    {
        var created = BuildProject();
        _projectStore.ExistsByNameAsync(created.Name, null, Arg.Any<CancellationToken>()).Returns(false);
        _projectStore
            .InsertAsync(created.Name, "https://git.example/repo", "https://raw.example/repo", Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await CreateSut().ExecuteAsync(
            created.Name, "bge-m3", 1024, "https://git.example/repo", "https://raw.example/repo");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_MissingName_ReturnsNameRequired(string? name)
    {
        var result = await CreateSut().ExecuteAsync(name, "bge-m3", 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-required");
    }

    [Fact]
    public async Task ExecuteAsync_NameTooLong_ReturnsNameTooLong()
    {
        var tooLong = new string('a', ProjectValidation.MaxNameLength + 1);

        var result = await CreateSut().ExecuteAsync(tooLong, "bge-m3", 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-too-long");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_MissingEmbeddingModel_ReturnsEmbeddingModelRequired(string? embeddingModel)
    {
        var result = await CreateSut().ExecuteAsync("proj", embeddingModel, 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-embedding-model-required");
    }

    [Fact]
    public async Task ExecuteAsync_MissingEmbeddingDimensions_ReturnsEmbeddingDimensionsRequired()
    {
        var result = await CreateSut().ExecuteAsync("proj", "bge-m3", null, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-embedding-dimensions-required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteAsync_NonPositiveEmbeddingDimensions_ReturnsEmbeddingDimensionsInvalid(int embeddingDimensions)
    {
        var result = await CreateSut().ExecuteAsync("proj", "bge-m3", embeddingDimensions, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-embedding-dimensions-invalid");
    }

    [Fact]
    public async Task ExecuteAsync_NameAlreadyUsed_ReturnsNameConflict()
    {
        _projectStore.ExistsByNameAsync("proj", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().ExecuteAsync("proj", "bge-m3", 1024, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("409-name-conflict");
        await _projectStore.DidNotReceive().InsertAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>());
    }

    private static Project BuildProject() => Project.Create(
        "proj",
        "https://git.example/repo",
        "https://raw.example/repo",
        EmbeddingModel.Create("bge-m3", 1024).Value,
        publicId: Guid.NewGuid(),
        id: 1).Value;

    private CreateProject CreateSut() => new(_projectStore);
}
