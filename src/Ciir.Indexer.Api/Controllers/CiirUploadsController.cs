using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Problems;
using Ciir.Indexer.Api.Uploads;
using Ciir.Indexer.Application;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Ciir.Indexer.Api.Controllers;

/// <summary>
/// The only way to get a CIIR JSONL file indexed: either <see cref="UploadAsync"/> streams it here
/// over HTTP, or <see cref="RegisterAsync"/> registers one the caller already placed directly in
/// object storage (the bring-your-own-upload path for files too large for a single HTTP request).
/// Both store/confirm the file in object storage for a background worker to pick up later, and
/// this also reports an upload's status. Contains no indexing logic of its own - it delegates all
/// validation to <see cref="SubmitCiirUpload"/>/<see cref="RegisterCiirUpload"/> and maps the
/// result to an HTTP response.
/// </summary>
// S6960: flags this controller for having three actions. That's the standard REST create+read
// pair for a single resource (submit/register an upload, check its status) plus a second create
// route for the same resource via a different ingestion path, not three responsibilities.
#pragma warning disable S6960
[ApiController]
[Route("api/indexer/ciir-uploads")]
public sealed class CiirUploadsController : ControllerBase
{
    /// <summary>The <c>Microsoft.AspNetCore.RateLimiting</c> policy name bounding concurrent uploads (upload spec §11).</summary>
    internal const string RateLimiterPolicyName = "ciir-uploads";

    // Safety margin above the configured file-size limit, covering multipart boundaries/headers
    // and the small "projectId" text field - the authoritative limit is enforced byte-by-byte on
    // the "ciirFile" section itself via SizeLimitedStream, not by this Kestrel-level cap alone.
    private const long RequestBodySizeMargin = 1024 * 1024;

    private readonly SubmitCiirUpload _submitCiirUpload;
    private readonly RegisterCiirUpload _registerCiirUpload;
    private readonly ICiirUploadStore _uploadStore;
    private readonly UploadOptions _options;

    /// <summary>Initializes a new instance of the <see cref="CiirUploadsController"/> class.</summary>
    /// <param name="submitCiirUpload">Validates the request and stores the uploaded file.</param>
    /// <param name="registerCiirUpload">Validates the request and registers an already-uploaded file.</param>
    /// <param name="uploadStore">Reads back an upload's current status for <see cref="GetAsync"/>.</param>
    /// <param name="options">Configures the maximum accepted file size.</param>
    public CiirUploadsController(
        SubmitCiirUpload submitCiirUpload,
        RegisterCiirUpload registerCiirUpload,
        ICiirUploadStore uploadStore,
        UploadOptions options)
    {
        _submitCiirUpload = submitCiirUpload;
        _registerCiirUpload = registerCiirUpload;
        _uploadStore = uploadStore;
        _options = options;
    }

    /// <summary>Submits a CIIR JSONL file for later indexation.</summary>
    /// <remarks>
    /// Expects a <c>multipart/form-data</c> body with a <c>projectId</c> text field naming an
    /// already-registered project, sent before a <c>ciirFile</c> file field - <c>projectId</c> must
    /// be validated before any byte of the file is stored, so it must arrive first in the stream.
    /// The file is streamed directly into object storage without ever being fully buffered in
    /// memory or on local disk, and is stored under a server-generated random key - never the
    /// client-supplied file name. This endpoint returns as soon as the file is stored, before a
    /// background worker has picked it up. Poll <c>GET /api/ciir-uploads/{uploadId}</c> with the id
    /// returned here to observe progress and the final outcome.
    /// </remarks>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>202 Accepted</c> with a <see cref="SubmitCiirUploadResponse"/> once the file is stored;
    /// <c>400</c> Problem Details when the request is not <c>multipart/form-data</c>, the
    /// <c>projectId</c>/<c>ciirFile</c> fields are missing or invalid, or the file's extension is
    /// not <c>.jsonl</c>; a body-less <c>404</c> when <c>projectId</c> does not reference a known
    /// project; <c>413</c> when the file exceeds the configured maximum size; <c>429</c> when the
    /// configured concurrent-upload limit is exceeded.
    /// </returns>
    [HttpPost]
    [EnableRateLimiting(RateLimiterPolicyName)]
    [DisableFormValueModelBinding]
    [ProducesResponseType<SubmitCiirUploadResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadAsync(CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return BadRequestProblem("Expected a 'multipart/form-data' request.");
        }

        var boundary = GetBoundary(Request.ContentType);
        if (boundary is null)
        {
            return BadRequestProblem("The multipart boundary is missing or invalid.");
        }

        AllowLargerRequestBody();

        var reader = new MultipartReader(boundary, Request.Body);
        string? projectId = null;

        var section = await reader.ReadNextSectionAsync(cancellationToken);
        while (section is not null)
        {
            var fileSection = section.AsFileSection();
            if (fileSection is not null && string.Equals(fileSection.Name, "ciirFile", StringComparison.OrdinalIgnoreCase))
            {
                return await SubmitAsync(projectId, fileSection.FileName, fileSection.FileStream!, cancellationToken);
            }

            var formSection = section.AsFormDataSection();
            if (formSection is not null && string.Equals(formSection.Name, "projectId", StringComparison.OrdinalIgnoreCase))
            {
                projectId = await formSection.GetValueAsync(cancellationToken);
            }

            section = await reader.ReadNextSectionAsync(cancellationToken);
        }

        return BadRequestProblem("The 'ciirFile' part is required.");
    }

    /// <summary>Registers a CIIR JSONL file already placed directly in object storage.</summary>
    /// <remarks>
    /// For files too large to push through <c>POST /api/ciir-uploads</c>'s own request body: upload
    /// the <c>.jsonl</c> file directly to this service's configured MinIO bucket yourself (e.g. via
    /// <c>mc cp</c>), then call this endpoint with the object key you uploaded it under. The
    /// object's existence is verified before registering it, so a typo in <c>objectKey</c> fails
    /// immediately rather than leaving a pending upload that can never be processed. From here on,
    /// this behaves exactly like <c>POST /api/ciir-uploads</c> - poll
    /// <c>GET /api/ciir-uploads/{uploadId}</c> with the id returned here to observe progress.
    /// </remarks>
    /// <param name="request">The already-registered project and the object key the file was uploaded under.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>202 Accepted</c> with a <see cref="SubmitCiirUploadResponse"/> once the upload is
    /// registered; <c>400</c> Problem Details when <c>projectId</c>/<c>objectKey</c> is missing or
    /// invalid, or the object key's extension is not <c>.jsonl</c>; a body-less <c>404</c> when
    /// <c>projectId</c> does not reference a known project, or when no object exists at
    /// <c>objectKey</c> in the configured bucket.
    /// </returns>
    [HttpPost("register")]
    [ProducesResponseType<SubmitCiirUploadResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterCiirUploadRequest request, CancellationToken cancellationToken)
    {
        var result = await _registerCiirUpload.ExecuteAsync(request.ProjectId, request.ObjectKey, cancellationToken);

        return result.Map(
            onSuccess: upload => (IActionResult)Accepted(new SubmitCiirUploadResponse(upload.Id, upload.Status.ToWireString())),
            onFailure: failure => failure.ToActionResult(HttpContext));
    }

    /// <summary>Reports the current status of one CIIR upload.</summary>
    /// <remarks>
    /// Intended to be polled after <c>POST /api/ciir-uploads</c> until <c>status</c> reaches a
    /// terminal value (<c>processed</c> or <c>failed</c>). Once <c>indexationId</c> is populated,
    /// <c>GET /api/indexations/{indexationId}</c> reports detailed indexation progress and counters.
    /// </remarks>
    /// <param name="id">The upload id returned by <c>POST /api/ciir-uploads</c>.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns><c>200 OK</c> with a <see cref="CiirUploadStatusResponse"/> when <paramref name="id"/> is known; a body-less <c>404</c> otherwise.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<CiirUploadStatusResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var upload = await _uploadStore.GetAsync(id, cancellationToken);

        return upload is null ? NotFound() : Ok(ToResponse(upload));
    }

    private static CiirUploadStatusResponse ToResponse(CiirUpload upload) => new(
        upload.Id,
        upload.ProjectId,
        upload.Status.ToWireString(),
        upload.CreatedAt,
        upload.ProcessingStartedAt,
        upload.ProcessedAt,
        upload.IndexingRunId,
        upload.Error);

    private static string? GetBoundary(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return null;
        }

        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        return string.IsNullOrWhiteSpace(boundary) ? null : boundary;
    }

    private async Task<IActionResult> SubmitAsync(
        string? projectId, string? fileName, Stream fileBody, CancellationToken cancellationToken)
    {
        try
        {
            var sizeLimitedBody = new SizeLimitedStream(fileBody, _options.MaxCiirFileSizeBytes);
            var result = await _submitCiirUpload.ExecuteAsync(
                projectId, fileName ?? string.Empty, sizeLimitedBody, contentLength: null, cancellationToken);

            return result.Map(
                onSuccess: upload => (IActionResult)Accepted(new SubmitCiirUploadResponse(upload.Id, upload.Status.ToWireString())),
                onFailure: failure => failure.ToActionResult(HttpContext));
        }
        catch (CiirUploadTooLargeException)
        {
            return ProblemResults.Build(
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                $"The file exceeds the maximum allowed size of {_options.MaxCiirFileSizeBytes} bytes.",
                Request.Path);
        }
    }

    private void AllowLargerRequestBody()
    {
        var maxRequestBodySizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is { IsReadOnly: false })
        {
            maxRequestBodySizeFeature.MaxRequestBodySize = _options.MaxCiirFileSizeBytes + RequestBodySizeMargin;
        }
    }

    private IActionResult BadRequestProblem(string detail) =>
        ProblemResults.Build(StatusCodes.Status400BadRequest, "Bad Request", detail, Request.Path);
}
#pragma warning restore S6960
