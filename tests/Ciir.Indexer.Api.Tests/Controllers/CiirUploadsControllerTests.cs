using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Controllers;
using Ciir.Indexer.Application;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using System.Text;

namespace Ciir.Indexer.Api.Tests.Controllers;

public sealed class CiirUploadsControllerTests
{
    private const long ExistingProjectId = 42;
    private const string Bucket = "ciir-uploads";

    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly ICiirUploadStore _uploadStore = Substitute.For<ICiirUploadStore>();
    private readonly UploadOptions _options = new() { MaxCiirFileSizeBytes = 1024 };

    public CiirUploadsControllerTests()
    {
        _projectStore
            .GetByIdAsync(ExistingProjectId, Arg.Any<CancellationToken>())
            .Returns(new Project
            {
                Id = ExistingProjectId,
                Name = "MyProject",
                EmbeddingModel = new EmbeddingModel("bge-m3", 1024),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

        // A real IObjectStorage adapter reads the stream through to completion, which is exactly
        // what makes SizeLimitedStream's mid-stream size check fire - a substitute that never
        // touches the stream would let an oversized upload through unnoticed.
        _objectStorage
            .UploadAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => DrainAsync(callInfo.ArgAt<Stream>(2), callInfo.ArgAt<CancellationToken>(5)));

        _objectStorage.ExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task UploadAsync_ValidRequest_ReturnsAcceptedWithTheUploadIdAndStatus()
    {
        var upload = BuildUpload();
        _uploadStore.CreateAsync(ExistingProjectId, Bucket, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(upload);
        var context = BuildMultipartHttpContext(projectId: "42", fileName: "ciir.jsonl", fileContent: "content"u8.ToArray());

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var body = accepted.Value.ShouldBeOfType<SubmitCiirUploadResponse>();
        body.UploadId.ShouldBe(upload.Id);
        body.Status.ShouldBe("pending");
    }

    [Fact]
    public async Task UploadAsync_MissingProjectIdPart_ReturnsBadRequestWithoutStoringAnything()
    {
        var context = BuildMultipartHttpContext(projectId: null, fileName: "ciir.jsonl", fileContent: "content"u8.ToArray(), includeProjectIdPart: false);

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _objectStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_MissingCiirFilePart_ReturnsBadRequest()
    {
        var context = BuildMultipartHttpContext(projectId: "42", fileName: null, fileContent: null, includeFilePart: false);

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadAsync_UnknownProject_ReturnsNotFoundWithoutStoringAnything()
    {
        var context = BuildMultipartHttpContext(projectId: "999", fileName: "ciir.jsonl", fileContent: "content"u8.ToArray());

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
        await _objectStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_WrongExtension_ReturnsBadRequest()
    {
        var context = BuildMultipartHttpContext(projectId: "42", fileName: "ciir.json", fileContent: "content"u8.ToArray());

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadAsync_FileLargerThanTheConfiguredLimit_ReturnsPayloadTooLarge()
    {
        var oversized = new byte[_options.MaxCiirFileSizeBytes + 1];
        var context = BuildMultipartHttpContext(projectId: "42", fileName: "ciir.jsonl", fileContent: oversized);

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        await _uploadStore.DidNotReceive().CreateAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_NotMultipartContentType_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";

        var result = await CreateSut(context).UploadAsync(CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_ReturnsAcceptedWithTheUploadIdAndStatus()
    {
        var upload = BuildUpload();
        _uploadStore.CreateAsync(ExistingProjectId, Bucket, "already-uploaded/ciir.jsonl", Arg.Any<CancellationToken>()).Returns(upload);

        var result = await CreateSut(new DefaultHttpContext())
            .RegisterAsync(new RegisterCiirUploadRequest("42", "already-uploaded/ciir.jsonl"), CancellationToken.None);

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var body = accepted.Value.ShouldBeOfType<SubmitCiirUploadResponse>();
        body.UploadId.ShouldBe(upload.Id);
        body.Status.ShouldBe("pending");
    }

    [Fact]
    public async Task RegisterAsync_ObjectDoesNotExist_ReturnsNotFoundWithoutRegisteringAnything()
    {
        _objectStorage.ExistsAsync(Bucket, "missing/ciir.jsonl", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut(new DefaultHttpContext())
            .RegisterAsync(new RegisterCiirUploadRequest("42", "missing/ciir.jsonl"), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
        await _uploadStore.DidNotReceive().CreateAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_UnknownProject_ReturnsNotFoundWithoutCheckingTheObject()
    {
        var result = await CreateSut(new DefaultHttpContext())
            .RegisterAsync(new RegisterCiirUploadRequest("999", "already-uploaded/ciir.jsonl"), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
        await _objectStorage.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_MissingObjectKey_ReturnsBadRequest()
    {
        var result = await CreateSut(new DefaultHttpContext())
            .RegisterAsync(new RegisterCiirUploadRequest("42", null), CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task RegisterAsync_WrongExtension_ReturnsBadRequest()
    {
        var result = await CreateSut(new DefaultHttpContext())
            .RegisterAsync(new RegisterCiirUploadRequest("42", "already-uploaded/ciir.json"), CancellationToken.None);

        var problem = result.ShouldBeOfType<ObjectResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task GetAsync_ExistingUpload_ReturnsOkWithTheMappedStatusResponse()
    {
        var upload = BuildUpload(status: CiirUploadStatus.Processed, indexingRunId: Guid.NewGuid());
        _uploadStore.GetAsync(upload.Id, Arg.Any<CancellationToken>()).Returns(upload);

        var result = await CreateSut(new DefaultHttpContext()).GetAsync(upload.Id, CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<CiirUploadStatusResponse>();
        body.Id.ShouldBe(upload.Id);
        body.ProjectId.ShouldBe(upload.ProjectId);
        body.Status.ShouldBe("processed");
        body.IndexationId.ShouldBe(upload.IndexingRunId);
    }

    [Fact]
    public async Task GetAsync_UnknownUpload_ReturnsNotFound()
    {
        _uploadStore.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CiirUpload?)null);

        var result = await CreateSut(new DefaultHttpContext()).GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    private static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        int bytesRead;
        do
        {
            bytesRead = await stream.ReadAsync(buffer, cancellationToken);
        }
        while (bytesRead > 0);
    }

    private static CiirUpload BuildUpload(CiirUploadStatus status = CiirUploadStatus.Pending, Guid? indexingRunId = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = ExistingProjectId,
        Bucket = Bucket,
        ObjectKey = $"{Guid.NewGuid():N}/ciir.jsonl",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        IndexingRunId = indexingRunId,
    };

    private static DefaultHttpContext BuildMultipartHttpContext(
        string? projectId,
        string? fileName,
        byte[]? fileContent,
        bool includeProjectIdPart = true,
        bool includeFilePart = true)
    {
        const string boundary = "test-boundary-1234";
        var body = new MemoryStream();

        void WriteLine(string text) => body.Write(Encoding.UTF8.GetBytes(text + "\r\n"));

        if (includeProjectIdPart)
        {
            WriteLine($"--{boundary}");
            WriteLine("Content-Disposition: form-data; name=\"projectId\"");
            WriteLine(string.Empty);
            WriteLine(projectId ?? string.Empty);
        }

        if (includeFilePart)
        {
            WriteLine($"--{boundary}");
            WriteLine($"Content-Disposition: form-data; name=\"ciirFile\"; filename=\"{fileName}\"");
            WriteLine("Content-Type: application/octet-stream");
            WriteLine(string.Empty);
            body.Write(fileContent ?? []);
            body.Write(Encoding.UTF8.GetBytes("\r\n"));
        }

        body.Write(Encoding.UTF8.GetBytes($"--{boundary}--\r\n"));
        body.Position = 0;

        return new DefaultHttpContext
        {
            Request =
            {
                ContentType = $"multipart/form-data; boundary={boundary}",
                Body = body,
            },
        };
    }

    private CiirUploadsController CreateSut(HttpContext context)
    {
        var submitCiirUpload = new SubmitCiirUpload(_projectStore, _objectStorage, _uploadStore, Bucket, _options);
        var registerCiirUpload = new RegisterCiirUpload(_projectStore, _objectStorage, _uploadStore, Bucket);
        return new CiirUploadsController(submitCiirUpload, registerCiirUpload, _uploadStore, _options)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }
}
