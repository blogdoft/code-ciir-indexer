using BlogDoFT.Libs.Api.Extensions;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Templates;

namespace Ciir.Indexer.Api.Logging;

/// <summary>
/// Structured JSON logging on stdout through Serilog. The sink is defined here in code, not in
/// configuration, so nothing can fall back to human-readable text regardless of environment; only
/// the levels come from the <c>Serilog:MinimumLevel</c> configuration section.
/// </summary>
internal static class StructuredLoggingExtensions
{
    // One JSON object per line. The level is emitted as "level" with the lowercase names Grafana/Loki
    // recognise (info/warn/error/critical...) - the default .NET JSON console formatter writes
    // "LogLevel": "Information" instead, which Loki's level detection ignores, so every line showed
    // up as "unknown". "..@p" spreads the message-template properties (and scope/enricher ones) as
    // top-level fields; "@tr"/"@sp" are the current Activity's trace and span ids.
    private const string HealthPath = "/health";
    private const string CorrelationIdHeader = "X-Correlation-ID";

    private const string JsonTemplate =
        "{ {timestamp: @t, " +
        "level: if @l = 'Verbose' then 'trace' " +
        "else if @l = 'Debug' then 'debug' " +
        "else if @l = 'Information' then 'info' " +
        "else if @l = 'Warning' then 'warn' " +
        "else if @l = 'Error' then 'error' " +
        "else 'critical', " +
        "message: @m, exception: @x, trace_id: @tr, span_id: @sp, ..@p} }\n";

    /// <summary>Replaces every logging provider with Serilog writing structured JSON to stdout.</summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .WriteTo.Console(new ExpressionTemplate(JsonTemplate)));

        return builder;
    }

    /// <summary>
    /// Resolves the request's correlation id (<c>X-Correlation-ID</c>, generated when the caller sent
    /// none), echoes it on the response and adds it to every log line written while the request runs
    /// as <c>correlation_id</c>. Register it ahead of <see cref="UseStructuredRequestLogging"/>, so the
    /// request log line carries it too.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication UseCorrelationId(this WebApplication app)
    {
        app.Use(async (httpContext, next) =>
        {
            var correlationId = httpContext.Request.Headers.GetCorrelationId();
            httpContext.Response.Headers[CorrelationIdHeader] = correlationId;

            using (LogContext.PushProperty("correlation_id", correlationId))
            {
                await next(httpContext);
            }
        });

        return app;
    }

    /// <summary>
    /// Logs one line per HTTP request, skipping the Kubernetes probe. Requests that end in an
    /// exception or a 5xx are logged as errors (the exception itself goes in <c>exception</c>).
    /// Register it ahead of everything that should be covered.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication UseStructuredRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevel);
        return app;
    }

    /// <summary>
    /// Creates a standalone logger with the same JSON format, for failures that happen before (or
    /// without) the host's own logging - e.g. a startup crash. The caller owns and must dispose it:
    /// the console sink writes synchronously, but disposing also flushes.
    /// </summary>
    /// <returns>A logger writing the same JSON lines as the host's.</returns>
    public static Serilog.Core.Logger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .WriteTo.Console(new ExpressionTemplate(JsonTemplate))
            .CreateLogger();

    // /health is below any configured Serilog:MinimumLevel, so the kubelet's periodic probe hits are
    // never written - the same effect as skipping the log entirely, without special-casing the sink.
    private static LogEventLevel RequestLogLevel(HttpContext httpContext, double elapsedMs, Exception? exception)
    {
        if (httpContext.Request.Path.StartsWithSegments(HealthPath))
        {
            return LogEventLevel.Verbose;
        }

        return exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    }
}
