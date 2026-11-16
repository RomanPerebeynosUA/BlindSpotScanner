using System.Globalization;
using System.Net;
using BlindSpotScanner.Application.GitHub;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.GitHub;

/// <summary>
/// Waits out GitHub rate limits: Retry-After (secondary limits) or x-ratelimit-reset when
/// x-ratelimit-remaining is 0 (primary limit). Retries at most <see cref="MaxRetries"/> times and
/// fails fast when the wait would exceed <see cref="GitHubOptions.MaxRateLimitWait"/>.
/// Any other 403 (no permission) is returned unchanged, without retrying.
/// </summary>
public sealed class GitHubRateLimitHandler(TimeProvider time, IOptions<GitHubOptions> options) : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan ResetMargin = TimeSpan.FromSeconds(1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var resetAt = RateLimitResetAt(response);
            if (resetAt is null)
            {
                return response;
            }

            response.Dispose();
            var wait = resetAt.Value - time.GetUtcNow();
            if (attempt == MaxRetries || wait > options.Value.MaxRateLimitWait)
            {
                throw new GitHubRateLimitException(resetAt.Value);
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>When the request may be retried, or null if the response is not a rate-limit response.</summary>
    private DateTimeOffset? RateLimitResetAt(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            return retryAfter.Delta is { } delta ? now + delta : retryAfter.Date ?? now;
        }

        if (Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var resetEpoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(resetEpoch) + ResetMargin;
        }

        return null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
