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
        _projectStore.GetByPublicIdAsync(project.PublicId, Arg.Any<CancellationToken>()).Returns(project);

        var result = await CreateSut().GetAsync(project.PublicId.ToString(), CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<ProjectResponse>();
        body.Id.ShouldBe(project.PublicId);
        body.Name.ShouldBe(project.Name);
    }

    [Fact]
    public async Task GetAsync_UnknownProject_ReturnsNotFound()
    {
        var unknownId = Guid.NewGuid();
        _projectStore.GetByPublicIdAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().GetAsync(unknownId.ToString(), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAsync_NonNumericId_ReturnsBadRequestWithoutQueryingTheStore()
    {
        var result = await CreateSut().GetAsync("not-a-guid", CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
        body.Id.ShouldBe(created.PublicId);
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
        _projectStore.GetByPublicIdAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _projectStore.ExistsByNameAsync("renamed", existing.Id, Arg.Any<CancellationToken>()).Returns(false);
        _projectStore
            .UpdateAsync(existing.Id, "renamed", null, null, Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(updated);

        var result = await CreateSut().UpdateAsync(
            existing.PublicId.ToString(), new ProjectUpdateRequest("renamed", "bge-m3", 1024), CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<ProjectResponse>();
        body.Name.ShouldBe("renamed");
    }

    [Fact]
    public async Task UpdateAsync_UnknownProject_ReturnsMappedFailure()
    {
        var unknownId = Guid.NewGuid();
        _projectStore.GetByPublicIdAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().UpdateAsync(
            unknownId.ToString(), new ProjectUpdateRequest("proj", "bge-m3", 1024), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateAsync_NonNumericId_ReturnsBadRequest()
    {
        var result = await CreateSut().UpdateAsync(
            "not-a-guid", new ProjectUpdateRequest("proj", "bge-m3", 1024), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task DeleteAsync_ExistingProject_ReturnsNoContent()
    {
        var existing = BuildProject(1);
        _projectStore.GetByPublicIdAsync(existing.PublicId, Arg.Any<CancellationToken>()).Returns(existing);
        _projectStore.DeleteAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().DeleteAsync(existing.PublicId.ToString(), CancellationToken.None);

        result.ShouldBeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteAsync_MissingProject_ReturnsMappedFailure()
    {
        var unknownId = Guid.NewGuid();
        _projectStore.GetByPublicIdAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().DeleteAsync(unknownId.ToString(), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteAsync_NonNumericId_ReturnsBadRequest()
    {
        var result = await CreateSut().DeleteAsync("not-a-guid", CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    private static Project BuildProject(long id) => Project.Create(
        $"Project{id}",
        gitUrl: null,
        gitRawUrl: null,
        EmbeddingModel.Create("bge-m3", 1024).Value,
        publicId: Guid.NewGuid(),
        id: id).Value;

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
