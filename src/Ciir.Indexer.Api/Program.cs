using BlogDoFT.Libs.Api.OpenTelemetry.Extensions;
using BlogDoFT.Libs.WarmUp.Extensions;
using Ciir.Indexer.Api.Authentication;
using Ciir.Indexer.Api.Controllers;
using Ciir.Indexer.Api.OpenApi;
using Ciir.Indexer.Api.Uploads;
using Ciir.Indexer.Api.WarmUp;
using Ciir.Indexer.Application;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Ollama;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI;
using Ciir.Indexer.Infrastructure.ObjectStorage.Minio;
using Ciir.Indexer.Infrastructure.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Instrumentation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// All logs are structured JSON on stdout - the only formatter registered, so nothing can fall
// back to human-readable text regardless of environment/configuration.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

try
{
    // "UseLogExporter" is "DoNotUse" so OpenTelemetry doesn't emit a second log stream alongside
    // the structured JSON console logs configured above.
    builder.Services.AddOtel(builder.Configuration);

    // The kubelet's probe hits are noise in the APM: dropping them here also drops their child
    // spans (the sampler follows the parent's decision), such as the readiness DB query.
    builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        options.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments("/health"));

    // --- Keycloak authentication (auth spec): opt-in. Null unless "Keycloak:Enabled" is true, in
    // which case no authentication is registered and every endpoint stays open. ---
    var keycloakOptions = KeycloakOptions.FromConfiguration(builder.Configuration);
    if (keycloakOptions is not null)
    {
        builder.Services.AddKeycloakAuthentication(keycloakOptions);
    }

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
        options.AddOperationTransformer<CiirUploadRequestBodyOperationTransformer>();
        if (keycloakOptions is not null)
        {
            options.AddDocumentTransformer<KeycloakSecurityDocumentTransformer>();
        }
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
        connectionString,
        new IndexerDatabaseOptions { EmbeddingDimensions = embeddingOptions.Dimensions, ConnectionString = connectionString });

    // Warm-up commands run in registration order, so the migrator (registered inside
    // AddPostgreSqlPersistence above) always runs before the reconciler below it.
    builder.Services.AddSingleton<OrphanedIndexingRunReconciler>();
    builder.Services.AddWarmUp();

    // --- MinIO object storage for CIIR uploads (upload spec §5/§10/§11). ---
    var minioOptions = builder.Configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>()
        ?? throw new InvalidOperationException($"Missing required configuration section '{MinioOptions.SectionName}'.");
    builder.Services.AddMinioObjectStorage(minioOptions);

    var uploadOptions = builder.Configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>() ?? new UploadOptions();
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

    builder.Services.AddCiirUploadsFeature(minioOptions, uploadOptions);

    var app = builder.Build();

    // Fail fast on invalid embedding configuration rather than waiting for the first request.
    app.Services.GetRequiredService<IEmbeddingGenerator>();

    // Fail fast on an unreachable/misconfigured MinIO bucket rather than on the first upload.
    await app.Services.GetRequiredService<IObjectStorage>().EnsureBucketExistsAsync(minioOptions.BucketName);

    // Always mapped (not gated to Development) so Swagger is reachable in this cluster too - both
    // routes live under "api/indexer" since that's the only prefix the blogdoft.home.arpa ingress
    // forwards to this service (see .eng/k8s/ingress.yaml). The swagger.json URL is relative
    // ("../openapi/...") rather than root-relative, so the browser resolves it against whatever
    // prefix it is actually browsing under (locally or through the ingress) without the app
    // needing to know about that prefix itself.
    //
    // When Keycloak authentication is on, everything else requires a token (fallback policy), but
    // the OpenAPI document and Swagger UI stay anonymous: a browser can't attach a Bearer token to
    // page navigation, and they carry no project/upload data (auth spec, "Exceções deliberadas").
    // UseSwaggerUI is middleware ahead of the authorization middleware, so it is anonymous by
    // position; the OpenAPI document is an endpoint and opts out explicitly.
    app.MapOpenApi("/api/indexer/openapi/{documentName}.json").AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "CIIR Indexer API");
        options.RoutePrefix = "api/indexer/swagger";

        // "Authorize" redirects to the Keycloak login (authorization code + PKCE, public client)
        // when a client id is configured (the API and Swagger UI share one Keycloak client); the redirect_uri, oauth2-redirect.html under this
        // same prefix, is computed by the browser from the current URL.
        if (keycloakOptions is { ClientId.Length: > 0 })
        {
            options.OAuthClientId(keycloakOptions.ClientId);
            options.OAuthUsePkce();
            options.OAuthScopes(KeycloakSecurityDocumentTransformer.OpenIdScope);
        }
    });

    // Authentication/authorization run before the rate limiter so an unauthenticated request is
    // refused with 401 before it can occupy one of the limited upload slots. /health stays
    // anonymous: the Kubernetes readiness probe calls it without a token.
    if (keycloakOptions is not null)
    {
        app.UseAuthentication();
    }

    app.UseAuthorization();
    app.UseRateLimiter();
    app.UseOpenTelemetry();
    app.MapHealthChecks("/health").ExcludeFromDescription().AllowAnonymous().DisableHttpMetrics();
    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex)
{
    // Configuration and database-connectivity problems (missing/invalid settings,
    // ConfigurationValidationException, DatabaseUnavailableException, host bind failures, ...) are
    // unrecoverable at startup: log why as structured JSON and terminate rather than serve traffic
    // in a broken state. The console logger writes from a background queue, and Environment.Exit
    // terminates the process without waiting for it - so the factory is disposed (which drains the
    // queue) before exiting, otherwise the message below would be lost.
    using (var loggerFactory = LoggerFactory.Create(logging => logging.AddJsonConsole()))
    {
        loggerFactory.CreateLogger("Ciir.Indexer.Api.Program")
            .LogCritical(ex, "Application failed to start and will terminate.");
    }

    Environment.Exit(1);
}
