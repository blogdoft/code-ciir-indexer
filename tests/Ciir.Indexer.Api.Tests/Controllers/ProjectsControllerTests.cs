using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Controllers;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Controllers;

public sealed class ProjectsControllerTests
{
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();

    [Fact]
    public async Task ListAsync_ValidRequest_ReturnsOkWithTheMappedPage()
    {
        var projects = new[] { BuildProject(1), BuildProject(2) };
        _projectStore.SearchAsync(null, 0, 20, Arg.Any<CancellationToken>()).Returns((projects, 2L));

        var result = await CreateSut().ListAsync(null, null, CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<ProjectListResponse>();
        body.Items.Count.ShouldBe(2);
        body.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ListAsync_InvalidPageSize_ReturnsBadRequestWithoutQueryingTheStore()
    {
        var result = await CreateSut().ListAsync(null, 0, CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _projectStore.DidNotReceive().SearchAsync(
            Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ExistingProject_ReturnsOkWithTheMappedResponse()
    {
        var project = BuildProject(1);
        _projectStore.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(project);

        var result = await CreateSut().GetAsync("1", CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<ProjectResponse>();
        body.Id.ShouldBe(1);
        body.Name.ShouldBe(project.Name);
    }

    [Fact]
    public async Task GetAsync_UnknownProject_ReturnsNotFound()
    {
        _projectStore.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().GetAsync("999", CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAsync_NonNumericId_ReturnsBadRequestWithoutQueryingTheStore()
    {
        var result = await CreateSut().GetAsync("not-a-number", CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _projectStore.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsCreatedAtRoute()
    {
        var created = BuildProject(1);
        _projectStore.ExistsByNameAsync("proj", null, Arg.Any<CancellationToken>()).Returns(false);
        _projectStore
            .InsertAsync("proj", null, null, Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await CreateSut().CreateAsync(new ProjectCreateRequest("proj", "bge-m3", 1024), CancellationToken.None);

        var createdAtRoute = result.ShouldBeOfType<CreatedAtRouteResult>();
        var body = createdAtRoute.Value.ShouldBeOfType<ProjectResponse>();
        body.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task CreateAsync_NameConflict_ReturnsMappedFailure()
    {
        _projectStore.ExistsByNameAsync("proj", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().CreateAsync(new ProjectCreateRequest("proj", "bge-m3", 1024), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task CreateAsync_MissingName_ReturnsBadRequestWithoutQueryingTheStore()
    {
        var result = await CreateSut().CreateAsync(new ProjectCreateRequest(null, "bge-m3", 1024), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _projectStore.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_ReturnsOkWithTheUpdatedProject()
    {
        var existing = BuildProject(1);
        var updated = existing with { Name = "renamed" };
        _projectStore.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(existing);
        _projectStore.ExistsByNameAsync("renamed", 1, Arg.Any<CancellationToken>()).Returns(false);
        _projectStore
            .UpdateAsync(1, "renamed", null, null, Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(updated);

        var result = await CreateSut().UpdateAsync(
            "1", new ProjectUpdateRequest("renamed", "bge-m3", 1024), CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<ProjectResponse>();
        body.Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task UpdateAsync_UnknownProject_ReturnsMappedFailure()
    {
        _projectStore.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().UpdateAsync(
            "999", new ProjectUpdateRequest("proj", "bge-m3", 1024), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateAsync_NonNumericId_ReturnsBadRequest()
    {
        var result = await CreateSut().UpdateAsync(
            "not-a-number", new ProjectUpdateRequest("proj", "bge-m3", 1024), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task DeleteAsync_ExistingProject_ReturnsNoContent()
    {
        _projectStore.DeleteAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().DeleteAsync("1", CancellationToken.None);

        result.ShouldBeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteAsync_MissingProject_ReturnsMappedFailure()
    {
        _projectStore.DeleteAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut().DeleteAsync("999", CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteAsync_NonNumericId_ReturnsBadRequest()
    {
        var result = await CreateSut().DeleteAsync("not-a-number", CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    private static Project BuildProject(long id) => new()
    {
        Id = id,
        Name = $"Project{id}",
        EmbeddingModel = new EmbeddingModel("bge-m3", 1024),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private ProjectsController CreateSut() =>
        new(
            new ListProjects(_projectStore),
            new CreateProject(_projectStore),
            new UpdateProject(_projectStore),
            new DeleteProject(_projectStore),
            _projectStore)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
}
