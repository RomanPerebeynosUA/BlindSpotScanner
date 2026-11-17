using BlindSpotScanner.Infrastructure.Logging;
using FluentAssertions;
using Serilog;
using Serilog.Formatting.Compact;

namespace BlindSpotScanner.Infrastructure.Tests.Logging;

/// <summary>Doc 10: secrets are never logged, by property name (the destructuring policy) or by pattern (the scrubbing formatter).</summary>
public class SecretRedactionTests
{
    private const string GitHubToken = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";

    private static string Log(Action<ILogger> write)
    {
        using var output = new StringWriter();
        using (var logger = new LoggerConfiguration()
            .Destructure.With<RedactingDestructuringPolicy>()
            .WriteTo.Sink(new FormatterSink(new ScrubbingFormatter(new CompactJsonFormatter()), output))
            .CreateLogger())
        {
            write(logger);
        }

        return output.ToString();
    }

    [Fact]
    public void Log_ObjectWithApiToken_IsRedacted()
    {
        // Act
        var line = Log(l => l.Information("Jira options {@Options}", new { BaseUrl = "https://example.atlassian.net", ApiToken = "jira-secret-value" }));

        // Assert
        new { Leaks = line.Contains("jira-secret-value", StringComparison.Ordinal), Redacted = line.Contains("«redacted»", StringComparison.Ordinal) }
            .Should().Be(new { Leaks = false, Redacted = true });
    }

    [Fact]
    public void Log_ExceptionMessageWithConnectionString_IsRedacted()
    {
        // Act
        var line = Log(l => l.Error(new InvalidOperationException("Cannot open Host=db;Username=u;Password=hunter2-secret"), "Database failed"));

        // Assert
        line.Should().NotContain("hunter2-secret");
    }

    [Fact]
    public void Log_GitHubTokenInMessage_IsRedacted()
    {
        // Act
        var line = Log(l => l.Warning("Calling GitHub with {Header}", "Bearer " + GitHubToken));

        // Assert
        line.Should().NotContain(GitHubToken);
    }

    [Fact]
    public void Log_OrdinaryProperty_IsKept()
    {
        // Act
        var line = Log(l => l.Information("Repository {@Repository}", new { Owner = "example-org", Name = "bookstore-api" }));

        // Assert
        line.Should().Contain("bookstore-api");
    }
}

/// <summary>Writes formatted events to a TextWriter (Serilog core has no TextWriter sink).</summary>
internal sealed class FormatterSink(Serilog.Formatting.ITextFormatter formatter, TextWriter output) : Serilog.Core.ILogEventSink
{
    public void Emit(Serilog.Events.LogEvent logEvent) => formatter.Format(logEvent, output);
}
