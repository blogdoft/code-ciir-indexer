using Ciir.Indexer.Api.Controllers;
using Ciir.Indexer.Api.OpenApi;
using Ciir.Indexer.Api.Uploads;
using Ciir.Indexer.Application;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Ollama;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI;
using Ciir.Indexer.Infrastructure.ObjectStorage.Minio;
using Ciir.Indexer.Infrastructure.PostgreSql;
using Ciir.Indexer.Infrastructure.PostgreSql.Migrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// All logs are structured JSON on stdout - the only formatter registered, so nothing can fall
// back to human-readable text regardless of environment/configuration.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

try
{
    builder.Services.AddControllers();
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        // Without this, [ApiController] rewrites a bare NotFoundResult() into a JSON Problem
        // Details body - every controller here documents 404 responses as body-less.
        options.SuppressMapClientErrors = true;
    });
    builder.Services.AddHealthChecks();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer<ApiInfoDocumentTransformer>();
        options.AddDocumentTransformer<ControllerTagDescriptionsDocumentTransformer>();
        options.AddDocumentTransformer<PublicServerDocumentTransformer>();
    });

    // --- Embeddings: register every provider module, then resolve the one configured provider once
    // (spec §34) so the rest of the application depends only on IEmbeddingGenerator. ---
    var embeddingOptions = builder.Configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>()
        ?? throw new InvalidOperationException($"Missing required configuration section '{EmbeddingOptions.SectionName}'.");

    builder.Services.AddOllamaEmbeddingProvider();
    builder.Services.AddOpenAiEmbeddingProviders();
    builder.Services.AddSingleton<EmbeddingGeneratorResolver>();
    builder.Services.AddSingleton(sp => sp.GetRequiredService<EmbeddingGeneratorResolver>().Resolve(embeddingOptions));

    // --- PostgreSQL persistence. The install-wide vector(N) width (spec §10) is derived from the same
    // Embeddings:Dimensions value the provider itself is configured with - never a second, independently
    // configured number that could silently drift out of sync with it. ---
    var connectionString = builder.Configuration.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Missing required connection string 'Database'.");

    builder.Services.AddPostgreSqlPersistence(
        connectionString, new IndexerDatabaseOptions { EmbeddingDimensions = embeddingOptions.Dimensions });

    // --- MinIO object storage for CIIR uploads (upload spec §5/§10/§11). ---
    var minioOptions = builder.Configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>()
        ?? throw new InvalidOperationException($"Missing required configuration section '{MinioOptions.SectionName}'.");
    builder.Services.AddMinioObjectStorage(minioOptions);

    var uploadOptions = builder.Configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>() ?? new UploadOptions();
    builder.Services.AddSingleton(uploadOptions);
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddConcurrencyLimiter(CiirUploadsController.RateLimiterPolicyName, limiterOptions =>
        {
            limiterOptions.PermitLimit = uploadOptions.MaxConcurrentUploads;
            limiterOptions.QueueLimit = 0;
        });
    });

    var indexingOptions = builder.Configuration.GetSection(IndexingOptions.SectionName).Get<IndexingOptions>() ?? new IndexingOptions();
    builder.Services.AddCiirIndexerApplication(indexingOptions);

    // --- CIIR uploads (upload spec §4/§8): the bucket name is read from the "Minio" section here,
    // in the composition root, rather than threaded through the Application layer, which must not
    // depend on an infrastructure-specific options type. Uploading (POST /api/ciir-uploads) and
    // registering an already-uploaded file (POST /api/ciir-uploads/register) are the only two ways
    // a CIIR file reaches indexation - there is no local-filesystem-path entry point. ---
    builder.Services.AddSingleton(sp => new SubmitCiirUpload(
        sp.GetRequiredService<IProjectStore>(),
        sp.GetRequiredService<IObjectStorage>(),
        sp.GetRequiredService<ICiirUploadStore>(),
        minioOptions.BucketName,
        uploadOptions));
    builder.Services.AddSingleton(sp => new RegisterCiirUpload(
        sp.GetRequiredService<IProjectStore>(),
        sp.GetRequiredService<IObjectStorage>(),
        sp.GetRequiredService<ICiirUploadStore>(),
        minioOptions.BucketName));
    builder.Services.AddSingleton(sp => new ProcessNextCiirUpload(
        sp.GetRequiredService<ICiirUploadStore>(),
        sp.GetRequiredService<IObjectStorage>(),
        sp.GetRequiredService<IProjectStore>(),
        sp.GetRequiredService<IIndexingRunStore>(),
        sp.GetRequiredService<IEmbeddingGenerator>(),
        sp.GetRequiredService<RunIndexation>(),
        uploadOptions,
        sp.GetRequiredService<ILogger<ProcessNextCiirUpload>>()));
    builder.Services.AddHostedService<CiirUploadWorker>();

    var app = builder.Build();

    // Fail fast on invalid embedding configuration rather than waiting for the first request.
    app.Services.GetRequiredService<IEmbeddingGenerator>();

    // Fail fast on an unreachable/misconfigured MinIO bucket rather than on the first upload.
    await app.Services.GetRequiredService<IObjectStorage>().EnsureBucketExistsAsync(minioOptions.BucketName);

    using (var migrationScope = app.Services.CreateScope())
    {
        migrationScope.ServiceProvider.GetRequiredService<DatabaseMigrator>().Apply();
    }

    // Recover from a crash mid-run (spec §27): mark any indexing run left in-progress by a
    // previous process lifetime as failed. A one-shot startup check, not an ongoing background
    // loop - every run is now started synchronously within CiirUploadWorker's own polling cycle
    // (ProcessNextCiirUpload -> RunIndexation), so there is no separate queue/worker to recover on.
    var reconciledRunIds = await app.Services.GetRequiredService<IIndexingRunStore>().ReconcileOrphanedRunsAsync();
    if (reconciledRunIds.Count > 0)
    {
        app.Logger.LogWarning("Marked {Count} orphaned indexing run(s) as failed on startup.", reconciledRunIds.Count);
    }

    // Always mapped (not gated to Development) so Swagger is reachable in this cluster too - both
    // routes live under "api/indexer" since that's the only prefix the blogdoft.home.arpa ingress
    // forwards to this service (see .eng/k8s/ingress.yaml). The swagger.json URL is relative
    // ("../openapi/...") rather than root-relative, so the browser resolves it against whatever
    // prefix it is actually browsing under (locally or through the ingress) without the app
    // needing to know about that prefix itself.
    app.MapOpenApi("/api/indexer/openapi/{documentName}.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "CIIR Indexer API");
        options.RoutePrefix = "api/indexer/swagger";
    });

    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapHealthChecks("/health").ExcludeFromDescription();
    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex)
{
    // Configuration and database-connectivity problems (missing/invalid settings,
    // ConfigurationValidationException, DatabaseUnavailableException, host bind failures, ...) are
    // unrecoverable at startup: log why as structured JSON and terminate rather than serve traffic
    // in a broken state.
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddJsonConsole());
    loggerFactory.CreateLogger("Ciir.Indexer.Api.Program")
        .LogCritical(ex, "Application failed to start and will terminate.");
    Environment.Exit(1);
}
