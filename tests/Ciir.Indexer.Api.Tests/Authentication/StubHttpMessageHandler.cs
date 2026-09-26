using System.Net;

namespace Ciir.Indexer.Api.Tests.Authentication;

/// <summary>An <see cref="HttpMessageHandler"/> that answers with a canned response (or exception) and records what it was sent.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpResponseMessage>? _respond;
    private readonly Exception? _throw;

    private StubHttpMessageHandler(Func<HttpResponseMessage>? respond, Exception? toThrow)
    {
        _respond = respond;
        _throw = toThrow;
    }

    public HttpMethod? RequestMethod { get; private set; }

    public Uri? RequestUri { get; private set; }

    public string? RequestContentType { get; private set; }

    public string? RequestBody { get; private set; }

    public static StubHttpMessageHandler Responding(HttpStatusCode status, string? body = null) =>
        new(() => new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty) }, null);

    public static StubHttpMessageHandler Throwing(Exception exception) => new(null, exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestMethod = request.Method;
        RequestUri = request.RequestUri;
        RequestContentType = request.Content?.Headers.ContentType?.MediaType;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return _throw is not null ? throw _throw : _respond!();
    }
}
