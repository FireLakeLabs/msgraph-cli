using System.Net;
using MsGraphCli.Core.Exceptions;

namespace MsGraphCli.Core.Graph;

/// <summary>
/// Delegating handler that retries on 429 (Too Many Requests) and 503 (Service Unavailable)
/// with Retry-After header support and exponential backoff.
/// </summary>
public sealed class RetryDelegatingHandler : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] BackoffDelays = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    public RetryDelegatingHandler() : base() { }
    public RetryDelegatingHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
            {
                return response;
            }

            if (attempt == MaxRetries)
            {
                break;
            }

            TimeSpan delay = GetDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }

        TimeSpan? retryAfter = response?.Headers.RetryAfter?.Delta;
        throw new RateLimitedException(
            $"Request failed after {MaxRetries} retries due to rate limiting.",
            retryAfter);
    }

    private static TimeSpan GetDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfterDelta)
        {
            return retryAfterDelta;
        }

        // Exponential backoff with jitter
        TimeSpan baseDelay = BackoffDelays[attempt];
        double jitter = Random.Shared.NextDouble() * 0.5 + 0.75; // 0.75x to 1.25x
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * jitter);
    }
}
