using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Problems;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Mvc;

namespace Ciir.Indexer.Api.Controllers;

/// <summary>
/// Full CRUD over the projects this service owns. Most projects are created/updated implicitly by
/// <c>POST /api/indexations</c> and <c>POST /api/ciir-uploads</c> (spec's "Atualização — Identidade
/// de projeto informada pelo chamador"), but this controller lets a caller manage them directly -
/// e.g. to register a project before its first indexation, or to update its git metadata without
/// re-running one.
/// </summary>
// S6960: flags this controller for exposing five actions. That's the standard REST CRUD verb set
// for a single resource (search, get, create, replace, delete), not five responsibilities -
// splitting it into multiple controllers would fragment one resource's operations for no benefit.
#pragma warning disable S6960
[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private const string GetProjectRouteName = "GetProject";

    private readonly ListProjects _listProjects;
    private readonly CreateProject _createProject;
    private readonly UpdateProject _updateProject;
    private readonly DeleteProject _deleteProject;
    private readonly IProjectStore _projectStore;

    /// <summary>Initializes a new instance of the <see cref="ProjectsController"/> class.</summary>
    /// <param name="listProjects">Validates and executes a paginated, name-filtered project search.</param>
    /// <param name="createProject">Validates and creates a new project.</param>
    /// <param name="updateProject">Validates and replaces an existing project.</param>
    /// <param name="deleteProject">Deletes an existing project.</param>
    /// <param name="projectStore">Reads back a single project by id for <see cref="GetAsync"/>.</param>
    public ProjectsController(
        ListProjects listProjects,
        CreateProject createProject,
        UpdateProject updateProject,
        DeleteProject deleteProject,
        IProjectStore projectStore)
    {
        _listProjects = listProjects;
        _createProject = createProject;
        _updateProject = updateProject;
        _deleteProject = deleteProject;
        _projectStore = projectStore;
    }

    /// <summary>Search projects (paginated).</summary>
    /// <remarks>
    /// Returns a page of projects. Optionally filter the results by project name using a partial,
    /// case-insensitive match. When no project matches the supplied filter (or no projects exist at
    /// all), the response is a 200 OK with an empty items array - this is not treated as an error.
    /// </remarks>
    /// <param name="page">Zero-based page number to retrieve. Defaults to 0. Must not be negative.</param>
    /// <param name="pageSize">Maximum number of projects per page. Defaults to 20, capped at 100. Must be a positive integer.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="ProjectListResponse"/>; <c>400</c> Problem Details when
    /// <c>name</c>/<paramref name="page"/>/<paramref name="pageSize"/> is invalid.
    /// </returns>
    [HttpGet]
    [ProducesResponseType<ProjectListResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        CancellationToken cancellationToken)
    {
        // Read the raw query value instead of a bound [FromQuery] parameter: MVC's default model
        // binding treats an empty string as null (ConvertEmptyStringToNull), which would make
        // "?name=" indistinguishable from omitting the parameter entirely - the latter means "no
        // filter", the former is an invalid empty filter.
#pragma warning disable S6932
        var name = Request.Query.TryGetValue("name", out var values) ? values.ToString() : null;
#pragma warning restore S6932

        var result = await _listProjects.ExecuteAsync(name, page, pageSize, cancellationToken);

        return result.Map(
            onSuccess: projectPage => (IActionResult)Ok(ToListResponse(projectPage)),
            onFailure: failure => failure.ToActionResult(HttpContext));
    }

    /// <summary>Get a project by id.</summary>
    /// <remarks>Returns a single project by its id.</remarks>
    /// <param name="projectId">
    /// Identifier of the project, corresponding to the id field returned by <c>GET /api/projects</c>.
    /// Must be a positive 64-bit integer; any other format results in a 400 response.
    /// </param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="ProjectResponse"/> when <paramref name="projectId"/> is
    /// known; <c>400</c> Problem Details when it is not a valid positive integer; a body-less
    /// <c>404</c> otherwise.
    /// </returns>
    [HttpGet("{projectId}", Name = GetProjectRouteName)]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!RouteId.TryParsePositive(projectId, "projectId", HttpContext.Request.Path, out var id, out var problem))
        {
            return problem!;
        }

        var project = await _projectStore.GetByIdAsync(id, cancellationToken);

        return project is null ? NotFound() : Ok(ToResponse(project));
    }

    /// <summary>Create a project.</summary>
    /// <remarks>Creates a new project with the given name, embedding model and embedding dimensions.</remarks>
    /// <param name="request">The name, embedding model and embedding dimensions of the project to create.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>201 Created</c> with the persisted <see cref="ProjectResponse"/>, including its generated
    /// id and timestamps; <c>400</c> Problem Details when the request body is missing/invalid;
    /// <c>409</c> Problem Details when a project with the given name already exists.
    /// </returns>
    [HttpPost]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> CreateAsync([FromBody] ProjectCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _createProject.ExecuteAsync(
            request.Name,
            request.EmbeddingModel,
            request.EmbeddingDimensions,
            request.GitUrl,
            request.GitRawUrl,
            cancellationToken);

        return result.Map(
            onSuccess: project => (IActionResult)CreatedAtRoute(GetProjectRouteName, new { projectId = project.Id }, ToResponse(project)),
            onFailure: failure => failure.ToActionResult(HttpContext));
    }

    /// <summary>Replace a project.</summary>
    /// <remarks>Replaces every field of an existing project. This is a full replace (PUT), not a partial patch - every field must be supplied.</remarks>
    /// <param name="projectId">
    /// Identifier of the project to update, corresponding to the id field returned by
    /// <c>GET /api/projects</c>. Must be a positive 64-bit integer; any other format results in a
    /// 400 response.
    /// </param>
    /// <param name="request">The project's new name, embedding model and embedding dimensions.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>200 OK</c> with the updated <see cref="ProjectResponse"/>; <c>400</c> Problem Details when
    /// <paramref name="projectId"/> or the request body is invalid; a body-less <c>404</c> when
    /// <paramref name="projectId"/> is unknown; <c>409</c> Problem Details when the new name already
    /// belongs to another project.
    /// </returns>
    [HttpPut("{projectId}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> UpdateAsync(
        string projectId,
        [FromBody] ProjectUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!RouteId.TryParsePositive(projectId, "projectId", HttpContext.Request.Path, out var id, out var problem))
        {
            return problem!;
        }

        var result = await _updateProject.ExecuteAsync(
            id,
            request.Name,
            request.EmbeddingModel,
            request.EmbeddingDimensions,
            request.GitUrl,
            request.GitRawUrl,
            cancellationToken);

        return result.Map(
            onSuccess: project => (IActionResult)Ok(ToResponse(project)),
            onFailure: failure => failure.ToActionResult(HttpContext));
    }

    /// <summary>Delete a project.</summary>
    /// <remarks>
    /// Permanently deletes a project. This does not delete the project's indexed CIIR
    /// documents/relations, which remain pointing at a project id that no longer exists.
    /// </remarks>
    /// <param name="projectId">
    /// Identifier of the project to delete, corresponding to the id field returned by
    /// <c>GET /api/projects</c>. Must be a positive 64-bit integer; any other format results in a
    /// 400 response.
    /// </param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>204 No Content</c> when the project was deleted; <c>400</c> Problem Details when
    /// <paramref name="projectId"/> is invalid; a body-less <c>404</c> when it is unknown.
    /// </returns>
    [HttpDelete("{projectId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!RouteId.TryParsePositive(projectId, "projectId", HttpContext.Request.Path, out var id, out var problem))
        {
            return problem!;
        }

        var result = await _deleteProject.ExecuteAsync(id, cancellationToken);

        return result.Map(
            onSuccess: _ => (IActionResult)NoContent(),
            onFailure: failure => failure.ToActionResult(HttpContext));
    }

    private static ProjectListResponse ToListResponse(ProjectPage page) => new(
        page.Items.Select(ToResponse).ToList(),
        page.Page,
        page.PageSize,
        page.TotalCount,
        page.TotalPages);

    private static ProjectResponse ToResponse(Project project) => new(
        project.Id,
        project.Name,
        project.EmbeddingModel.Name,
        project.EmbeddingModel.Dimensions,
        project.GitUrl,
        project.GitRawUrl,
        project.CreatedAt,
        project.UpdatedAt);
}
#pragma warning restore S6960
