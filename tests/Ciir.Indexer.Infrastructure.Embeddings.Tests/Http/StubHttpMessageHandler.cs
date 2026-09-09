using System.Net;
using System.Text;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests.Http;

/// <summary>Replays a fixed sequence of responses, one per request, so retry/backoff logic can be tested without any real network call.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses;

    public StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses)
    {
        _responses = new Queue<Func<HttpResponseMessage>>(responses);
    }

    public int RequestCount { get; private set; }

    public static HttpResponseMessage JsonOk(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Status(HttpStatusCode statusCode, string body = "{}") => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No more stubbed responses.");
        }

        return Task.FromResult(_responses.Dequeue()());
    }
}
