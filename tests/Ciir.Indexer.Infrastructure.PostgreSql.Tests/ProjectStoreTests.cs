using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class ProjectStoreTests
{
    private readonly IProjectStore _sut;

    public ProjectStoreTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<IProjectStore>();
    }

    [Fact]
    public async Task EnsureProjectAsync_NewProject_CreatesIt()
    {
        var name = TestData.NewProjectName();

        var project = await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));

        project.Id.ShouldBeGreaterThan(0);
        project.Name.ShouldBe(name);
        project.EmbeddingModel.Name.ShouldBe("bge-m3");
        project.EmbeddingModel.Dimensions.ShouldBe(1024);
    }

    [Fact]
    public async Task EnsureProjectAsync_SameNameTwice_ReturnsTheSameProjectId()
    {
        var name = TestData.NewProjectName();

        var first = await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));
        var second = await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));

        second.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task EnsureProjectAsync_ModelChanged_UpdatesTheStoredModel()
    {
        var name = TestData.NewProjectName();
        await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));

        var updated = await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("nomic-embed-text", 768));

        updated.EmbeddingModel.Name.ShouldBe("nomic-embed-text");
        updated.EmbeddingModel.Dimensions.ShouldBe(768);
    }

    [Fact]
    public async Task EnsureProjectAsync_NewProject_PersistsGitUrls()
    {
        var name = TestData.NewProjectName();

        var project = await _sut.EnsureProjectAsync(
            name, "https://git.example/repo", "https://raw.example/repo", new EmbeddingModel("bge-m3", 1024));

        project.GitUrl.ShouldBe("https://git.example/repo");
        project.GitRawUrl.ShouldBe("https://raw.example/repo");
    }

    [Fact]
    public async Task EnsureProjectAsync_ReUpsert_UpdatesGitUrls()
    {
        var name = TestData.NewProjectName();
        await _sut.EnsureProjectAsync(
            name, "https://git.example/old", "https://raw.example/old", new EmbeddingModel("bge-m3", 1024));

        var updated = await _sut.EnsureProjectAsync(
            name, "https://git.example/new", "https://raw.example/new", new EmbeddingModel("bge-m3", 1024));

        updated.GitUrl.ShouldBe("https://git.example/new");
        updated.GitRawUrl.ShouldBe("https://raw.example/new");
    }
}
