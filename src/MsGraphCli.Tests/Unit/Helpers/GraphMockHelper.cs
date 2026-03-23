using System.Net;
using System.Text;
using Microsoft.Graph;

namespace MsGraphCli.Tests.Unit.Helpers;

/// <summary>
/// HTTP message handler that captures requests and returns pre-configured responses.
/// Used to test service-layer logic without hitting the real Graph API.
/// </summary>
internal sealed class MockGraphHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<CapturedRequest> Requests { get; } = [];
    public CapturedRequest LastRequest => Requests[^1];

    public void Enqueue(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    public void EnqueueEmpty(HttpStatusCode statusCode = HttpStatusCode.NoContent)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode));
    }

    public void EnqueueBytes(byte[] data, string contentType = "application/octet-stream",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(data)
            {
                Headers = { { "Content-Type", contentType } },
            },
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, request.Headers));

        if (_responses.Count > 0)
        {
            return _responses.Dequeue();
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
    }

    public static GraphServiceClient CreateClient(MockGraphHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0"),
        };
        return new GraphServiceClient(httpClient);
    }
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    System.Net.Http.Headers.HttpRequestHeaders Headers);
