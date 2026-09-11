using Ciir.Indexer.Api.Indexation;
using Ciir.Indexer.Api.Input;
using Ciir.Indexer.Api.OpenApi;
using Ciir.Indexer.Application;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Ollama;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI;
using Ciir.Indexer.Infrastructure.PostgreSql;
using Ciir.Indexer.Infrastructure.PostgreSql.Migrations;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// All logs are structured JSON on stdout - the only formatter registered, so nothing can fall
// back to human-readable text regardless of environment/configuration.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

try
{
    builder.Services.AddControllers();
    builder.Services.AddHealthChecks();
    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer<ApiInfoDocumentTransformer>();
        options.AddDocumentTransformer<ControllerTagDescriptionsDocumentTransformer>();
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

    // --- Path validation (spec §42) and the Application use-case layer. ---
    var pathOptions = builder.Configuration.GetSection(IndexerPathOptions.SectionName).Get<IndexerPathOptions>()
        ?? throw new InvalidOperationException($"Missing required configuration section '{IndexerPathOptions.SectionName}'.");
    builder.Services.AddSingleton(pathOptions);
    builder.Services.AddSingleton<IInputResolver, InputPathResolver>();

    var indexingOptions = builder.Configuration.GetSection(IndexingOptions.SectionName).Get<IndexingOptions>() ?? new IndexingOptions();
    builder.Services.AddCiirIndexerApplication(indexingOptions);

    // --- Background execution (spec §2/§33): a bounded channel consumed by a single worker. ---
    var queueCapacity = builder.Configuration.GetValue<int?>("Indexer:IndexationQueueCapacity") ?? 100;
    builder.Services.AddSingleton(new IndexationChannel(queueCapacity));
    builder.Services.AddSingleton<IIndexationQueue>(sp => sp.GetRequiredService<IndexationChannel>());
    builder.Services.AddHostedService<IndexationWorker>();

    var app = builder.Build();

    // Fail fast on invalid embedding configuration rather than waiting for the first request.
    app.Services.GetRequiredService<IEmbeddingGenerator>();

    using (var migrationScope = app.Services.CreateScope())
    {
        migrationScope.ServiceProvider.GetRequiredService<DatabaseMigrator>().Apply();
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

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
