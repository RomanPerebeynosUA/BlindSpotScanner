using System.Globalization;
using System.Net;
using BlindSpotScanner.Application.GitHub;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.GitHub;

/// <summary>
/// Waits out GitHub rate limits: Retry-After, x-ratelimit-reset when x-ratelimit-remaining is 0
/// (primary limit), or one minute for a 429 without headers or a 403 whose body names a secondary
/// rate limit (GitHub's advice). Waits are measured against the response's Date header, so local
/// clock skew doesn't shorten or stretch them. Retries at most <see cref="MaxRetries"/> times and
/// fails fast when the wait would exceed <see cref="GitHubOptions.MaxRateLimitWait"/>.
/// Any other 403 (no permission) is returned unchanged, without retrying.
/// </summary>
public sealed class GitHubRateLimitHandler(TimeProvider time, IOptions<GitHubOptions> options) : DelegatingHandler
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan ResetMargin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SecondaryLimitWait = TimeSpan.FromMinutes(1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var wait = await RateLimitWaitAsync(response, cancellationToken).ConfigureAwait(false);
            if (wait is null)
            {
                return response;
            }

            response.Dispose();
            if (attempt == MaxRetries || wait > options.Value.MaxRateLimitWait)
            {
                throw new GitHubRateLimitException(time.GetUtcNow() + wait.Value);
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait.Value, time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>How long to wait before retrying, or null if the response is not a rate-limit response.</summary>
    private async Task<TimeSpan?> RateLimitWaitAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return null;
        }

        var serverNow = response.Headers.Date ?? time.GetUtcNow();
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            return retryAfter.Delta ?? (retryAfter.Date is { } date ? date - serverNow : TimeSpan.Zero);
        }

        if (Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var resetEpoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(resetEpoch) + ResetMargin - serverNow;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return SecondaryLimitWait;
        }

        // A secondary-limit 403 may carry only a message. Buffer the body so a permission 403 stays readable.
        await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return body.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase) ? SecondaryLimitWait : null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
