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
            .InsertAsync(created.Name, "https://git.example/repo", "https://raw.example/repo", Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await CreateSut().ExecuteAsync(
            created.Name, "https://git.example/repo", "https://raw.example/repo");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_MissingName_ReturnsNameRequired(string? name)
    {
        var result = await CreateSut().ExecuteAsync(name, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-required");
    }

    [Fact]
    public async Task ExecuteAsync_NameTooLong_ReturnsNameTooLong()
    {
        var tooLong = new string('a', ProjectValidation.MaxNameLength + 1);

        var result = await CreateSut().ExecuteAsync(tooLong, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-name-too-long");
    }

    [Fact]
    public async Task ExecuteAsync_NameAlreadyUsed_ReturnsNameConflict()
    {
        _projectStore.ExistsByNameAsync("proj", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().ExecuteAsync("proj", null, null);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("409-name-conflict");
        await _projectStore.DidNotReceive().InsertAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static Project BuildProject() => Project.Create(
        "proj",
        "https://git.example/repo",
        "https://raw.example/repo",
        publicId: Guid.NewGuid(),
        id: 1).Value;

    private CreateProject CreateSut() => new(_projectStore);
}
