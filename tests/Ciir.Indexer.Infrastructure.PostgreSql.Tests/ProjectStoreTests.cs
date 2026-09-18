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

    [Fact]
    public async Task GetByIdAsync_ExistingProject_ReturnsIt()
    {
        var name = TestData.NewProjectName();
        var created = await _sut.EnsureProjectAsync(
            name, "https://git.example/repo", "https://raw.example/repo", new EmbeddingModel("bge-m3", 1024));

        var found = await _sut.GetByIdAsync(created.Id);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(created.Id);
        found.Name.ShouldBe(name);
        found.GitUrl.ShouldBe("https://git.example/repo");
        found.GitRawUrl.ShouldBe("https://raw.example/repo");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var found = await _sut.GetByIdAsync(-1);

        found.ShouldBeNull();
    }

    [Fact]
    public async Task SearchAsync_NoFilter_ReturnsMatchingProjectsAndTotalCount()
    {
        var name = TestData.NewProjectName();
        var created = await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));

        var (items, totalCount) = await _sut.SearchAsync(name, page: 0, pageSize: 20);

        items.ShouldContain(p => p.Id == created.Id);
        totalCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchAsync_NameFilter_OnlyReturnsMatchingProjects()
    {
        var name = TestData.NewProjectName();
        await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));
        var otherName = TestData.NewProjectName();
        await _sut.EnsureProjectAsync(otherName, null, null, new EmbeddingModel("bge-m3", 1024));

        var (items, totalCount) = await _sut.SearchAsync(name, page: 0, pageSize: 20);

        totalCount.ShouldBe(1);
        items.ShouldAllBe(p => p.Name == name);
    }

    [Fact]
    public async Task ExistsByNameAsync_ExistingName_ReturnsTrue()
    {
        var name = TestData.NewProjectName();
        await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));

        var exists = await _sut.ExistsByNameAsync(name, excludingId: null);

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsByNameAsync_UnknownName_ReturnsFalse()
    {
        var exists = await _sut.ExistsByNameAsync(TestData.NewProjectName(), excludingId: null);

        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task ExistsByNameAsync_ExcludingItsOwnId_ReturnsFalse()
    {
        var name = TestData.NewProjectName();
        var created = await _sut.EnsureProjectAsync(name, null, null, new EmbeddingModel("bge-m3", 1024));

        var exists = await _sut.ExistsByNameAsync(name, excludingId: created.Id);

        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task InsertAsync_NewName_CreatesAndReturnsIt()
    {
        var name = TestData.NewProjectName();

        var project = await _sut.InsertAsync(
            name, "https://git.example/repo", "https://raw.example/repo", new EmbeddingModel("bge-m3", 1024));

        project.Id.ShouldBeGreaterThan(0);
        project.Name.ShouldBe(name);
        project.GitUrl.ShouldBe("https://git.example/repo");
        project.GitRawUrl.ShouldBe("https://raw.example/repo");
        project.EmbeddingModel.Name.ShouldBe("bge-m3");
        project.EmbeddingModel.Dimensions.ShouldBe(1024);
        project.CreatedAt.ShouldNotBe(default);
        project.UpdatedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task UpdateAsync_ExistingProject_ReplacesEveryFieldAndReturnsIt()
    {
        var created = await _sut.InsertAsync(
            TestData.NewProjectName(), null, null, new EmbeddingModel("bge-m3", 1024));
        var newName = TestData.NewProjectName();

        var updated = await _sut.UpdateAsync(
            created.Id, newName, "https://git.example/new", "https://raw.example/new", new EmbeddingModel("nomic-embed-text", 768));

        updated.ShouldNotBeNull();
        updated.Name.ShouldBe(newName);
        updated.GitUrl.ShouldBe("https://git.example/new");
        updated.GitRawUrl.ShouldBe("https://raw.example/new");
        updated.EmbeddingModel.Name.ShouldBe("nomic-embed-text");
        updated.EmbeddingModel.Dimensions.ShouldBe(768);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        var updated = await _sut.UpdateAsync(
            -1, TestData.NewProjectName(), null, null, new EmbeddingModel("bge-m3", 1024));

        updated.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_ExistingProject_RemovesItAndReturnsTrue()
    {
        var created = await _sut.InsertAsync(
            TestData.NewProjectName(), null, null, new EmbeddingModel("bge-m3", 1024));

        var deleted = await _sut.DeleteAsync(created.Id);

        deleted.ShouldBeTrue();
        (await _sut.GetByIdAsync(created.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        var deleted = await _sut.DeleteAsync(-1);

        deleted.ShouldBeFalse();
    }
}
