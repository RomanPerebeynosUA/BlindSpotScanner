using System.Net;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Infrastructure.GitHub;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BlindSpotScanner.Infrastructure.Tests.GitHub;

/// <summary>
/// Uses an in-process stub instead of WireMock: the stub completes synchronously, so the handler's
/// delay timer exists before the test advances the fake clock (deterministic, no socket race).
/// </summary>
public class GitHubRateLimitHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);

    private static HttpResponseMessage PrimaryLimit(DateTimeOffset resetAt)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", resetAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    private static HttpResponseMessage RetryAfter(int seconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("Retry-After", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    private (HttpMessageInvoker Invoker, StubHandler Stub) Create(params Func<HttpResponseMessage>[] responses)
    {
        var stub = new StubHandler(responses);
        var handler = new GitHubRateLimitHandler(_time, Options.Create(new GitHubOptions())) { InnerHandler = stub };
        return (new HttpMessageInvoker(handler), stub);
    }

    private static HttpRequestMessage Get() => new(HttpMethod.Get, "https://api.github.com/repos/example-org/bookstore-api");

    [Fact]
    public async Task Send_PrimaryLimitExhausted_WaitsUntilResetThenRetries()
    {
        // Arrange
        var (invoker, _) = Create(() => PrimaryLimit(Now.AddSeconds(30)), () => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var send = invoker.SendAsync(Get(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(30));
        var completedAtReset = send.IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(1));
        var response = await send;

        // Assert
        new { completedAtReset, response.StatusCode }.Should().BeEquivalentTo(new { completedAtReset = false, StatusCode = HttpStatusCode.OK });
    }

    [Fact]
    public async Task Send_RetryAfter_WaitsThatLong()
    {
        // Arrange
        var (invoker, _) = Create(() => RetryAfter(5), () => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var send = invoker.SendAsync(Get(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(4));
        var completedEarly = send.IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(1));
        var response = await send;

        // Assert
        new { completedEarly, response.StatusCode }.Should().BeEquivalentTo(new { completedEarly = false, StatusCode = HttpStatusCode.OK });
    }

    [Fact]
    public async Task Send_ResetBeyondMaxWait_ThrowsRateLimitWithoutWaiting()
    {
        // Arrange
        var resetAt = Now.AddMinutes(10);
        var (invoker, stub) = Create(() => PrimaryLimit(resetAt));

        // Act
        var act = () => invoker.SendAsync(Get(), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubRateLimitException>()).Which.ResetAt.Should().Be(resetAt.AddSeconds(1));
        stub.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Send_RateLimitedFourTimes_ThrowsAfterThreeRetries()
    {
        // Arrange
        var (invoker, stub) = Create(() => RetryAfter(0));

        // Act
        var act = () => invoker.SendAsync(Get(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GitHubRateLimitException>();
        stub.Calls.Should().Be(4);
    }

    [Fact]
    public async Task Send_ForbiddenWithoutRateLimitHeaders_IsNotRetried()
    {
        // Arrange
        var (invoker, stub) = Create(() => new HttpResponseMessage(HttpStatusCode.Forbidden));

        // Act
        var response = await invoker.SendAsync(Get(), CancellationToken.None);

        // Assert
        new { response.StatusCode, stub.Calls }.Should().BeEquivalentTo(new { StatusCode = HttpStatusCode.Forbidden, Calls = 1 });
    }

    [Fact]
    public async Task Send_TooManyRequestsWithoutHeaders_WaitsOneMinute()
    {
        // Arrange
        var (invoker, _) = Create(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests), () => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var send = invoker.SendAsync(Get(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(59));
        var completedEarly = send.IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(1));
        var response = await send;

        // Assert
        new { completedEarly, response.StatusCode }.Should().BeEquivalentTo(new { completedEarly = false, StatusCode = HttpStatusCode.OK });
    }

    [Fact]
    public async Task Send_SecondaryLimitForbiddenBody_WaitsOneMinute()
    {
        // Arrange
        var (invoker, _) = Create(
            () => new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"message\":\"You have exceeded a secondary rate limit. Please wait a few minutes before you try again.\"}"),
            },
            () => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var send = invoker.SendAsync(Get(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(59));
        var completedEarly = send.IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(1));
        var response = await send;

        // Assert
        new { completedEarly, response.StatusCode }.Should().BeEquivalentTo(new { completedEarly = false, StatusCode = HttpStatusCode.OK });
    }

    [Fact]
    public async Task Send_ServerClockAhead_WaitsRelativeToServerDate()
    {
        // Arrange (server clock is 100s ahead; reset is 30s after server time, so the wait is 31s, not 131s)
        var (invoker, _) = Create(
            () =>
            {
                var limited = PrimaryLimit(Now.AddSeconds(130));
                limited.Headers.Date = Now.AddSeconds(100);
                return limited;
            },
            () => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var send = invoker.SendAsync(Get(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(31));
        var response = await send;

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Send_ForbiddenWithoutRateLimit_BodyStaysReadable()
    {
        // Arrange
        var (invoker, _) = Create(() => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"Must have admin rights\"}") });

        // Act
        var response = await invoker.SendAsync(Get(), CancellationToken.None);

        // Assert
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"message\":\"Must have admin rights\"}");
    }

    /// <summary>Returns the n-th response factory on the n-th call (the last one repeats).</summary>
    private sealed class StubHandler(Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(responses[Math.Min(Calls, responses.Length) - 1]());
        }
    }
}
