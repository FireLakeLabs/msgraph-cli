using System.Net;
using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Exceptions;
using FireLakeLabs.MsGraphCli.Core.Graph;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

public class RetryHandlerTests
{
    [Fact]
    public async Task SendAsync_SuccessOnFirstAttempt_ReturnsResponse()
    {
        var mockHandler = new MockHttpMessageHandler(
            [new HttpResponseMessage(HttpStatusCode.OK)]);

        var retryHandler = new RetryDelegatingHandler(mockHandler);
        var client = new HttpClient(retryHandler);

        HttpResponseMessage response = await client.GetAsync("https://graph.microsoft.com/v1.0/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_429ThenSuccess_RetriesAndReturns()
    {
        var responses = new[]
        {
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1)) }
            },
            new HttpResponseMessage(HttpStatusCode.OK),
        };

        var mockHandler = new MockHttpMessageHandler(responses);
        var retryHandler = new RetryDelegatingHandler(mockHandler);
        var client = new HttpClient(retryHandler);

        HttpResponseMessage response = await client.GetAsync("https://graph.microsoft.com/v1.0/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_503ThenSuccess_RetriesAndReturns()
    {
        var responses = new[]
        {
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1)) }
            },
            new HttpResponseMessage(HttpStatusCode.OK),
        };

        var mockHandler = new MockHttpMessageHandler(responses);
        var retryHandler = new RetryDelegatingHandler(mockHandler);
        var client = new HttpClient(retryHandler);

        HttpResponseMessage response = await client.GetAsync("https://graph.microsoft.com/v1.0/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mockHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_AllRetriesFail_ThrowsRateLimitedException()
    {
        var responses = Enumerable.Range(0, 4)
            .Select(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1)) }
            })
            .ToArray();

        var mockHandler = new MockHttpMessageHandler(responses);
        var retryHandler = new RetryDelegatingHandler(mockHandler);
        var client = new HttpClient(retryHandler);

        Func<Task> act = () => client.GetAsync("https://graph.microsoft.com/v1.0/me");

        await act.Should().ThrowAsync<RateLimitedException>()
            .WithMessage("*rate limiting*");
        mockHandler.CallCount.Should().Be(4); // 1 initial + 3 retries
    }

    [Fact]
    public async Task SendAsync_NonRetryableStatusCode_ReturnsImmediately()
    {
        var mockHandler = new MockHttpMessageHandler(
            [new HttpResponseMessage(HttpStatusCode.BadRequest)]);

        var retryHandler = new RetryDelegatingHandler(mockHandler);
        var client = new HttpClient(retryHandler);

        HttpResponseMessage response = await client.GetAsync("https://graph.microsoft.com/v1.0/me");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        mockHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_Cancellation_ThrowsOperationCanceled()
    {
        var responses = new[]
        {
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)) }
            },
            new HttpResponseMessage(HttpStatusCode.OK),
        };

        var mockHandler = new MockHttpMessageHandler(responses);
        var retryHandler = new RetryDelegatingHandler(mockHandler);
        var client = new HttpClient(retryHandler);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Func<Task> act = () => client.GetAsync("https://graph.microsoft.com/v1.0/me", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] _responses;
        public int CallCount { get; private set; }

        public MockHttpMessageHandler(HttpResponseMessage[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = _responses[CallCount];
            CallCount++;
            return Task.FromResult(response);
        }
    }
}
