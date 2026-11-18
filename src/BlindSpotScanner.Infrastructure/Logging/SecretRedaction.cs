using System.Collections;
using System.Reflection;
using BlindSpotScanner.Infrastructure.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace BlindSpotScanner.Infrastructure.Logging;

/// <summary>
/// Doc 10: secrets are never logged. Destructured objects lose properties whose names say they
/// hold a secret (Token, ApiKey, ApiToken, ClientSecret, Password, Secret, Authorization).
/// </summary>
public sealed class RedactingDestructuringPolicy : IDestructuringPolicy
{
    private static readonly string[] SecretNames = ["Token", "ApiKey", "ApiToken", "ClientSecret", "Password", "Secret", "Authorization"];

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        var type = value.GetType();
        if (value is string or IEnumerable || type.IsPrimitive || type.IsEnum || type.IsValueType && type.Namespace == "System")
        {
            result = null!;
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0);
        result = new StructureValue(
            [.. properties.Select(p => new LogEventProperty(p.Name, IsSecret(p.Name)
                ? new ScalarValue(SecretScrubber.Redacted)
                : propertyValueFactory.CreatePropertyValue(p.GetValue(value), destructureObjects: true)))],
            type.Name.StartsWith('<') ? null : type.Name);
        return true;
    }

    private static bool IsSecret(string name) => SecretNames.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Runs <see cref="SecretScrubber"/> over every formatted event: messages, properties and exceptions.</summary>
public sealed class ScrubbingFormatter(ITextFormatter inner) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        inner.Format(logEvent, buffer);
        output.Write(SecretScrubber.Scrub(buffer.ToString()));
    }
}

public static class LoggingSetup
{
    /// <summary>Serilog compact JSON to the console (doc 10), with secret redaction; levels from the "Serilog" section.</summary>
    public static IServiceCollection AddBlindSpotScannerLogging(this IServiceCollection services, IHostApplicationBuilder builder) =>
        services.AddSerilog((_, configuration) => Configure(configuration, builder.Configuration, output: null));

    /// <summary>
    /// The one logger setup. Configuration may only set levels (<c>Serilog:MinimumLevel:Default</c> and
    /// <c>:Override:*</c>): a sink added there would bypass the redaction, so sinks are not read from it.
    /// </summary>
    /// <param name="output">Where to write; null is the console.</param>
    public static LoggerConfiguration Configure(LoggerConfiguration logger, IConfiguration configuration, TextWriter? output)
    {
        var levels = configuration.GetSection("Serilog:MinimumLevel");
        logger.MinimumLevel.Is(Level(levels["Default"]) ?? LogEventLevel.Information);
        foreach (var source in levels.GetSection("Override").GetChildren())
        {
            if (Level(source.Value) is { } level)
            {
                logger.MinimumLevel.Override(source.Key, level);
            }
        }

        var formatter = new ScrubbingFormatter(new CompactJsonFormatter());
        logger.Destructure.With<RedactingDestructuringPolicy>().Enrich.FromLogContext();
        return output is null ? logger.WriteTo.Console(formatter) : logger.WriteTo.Sink(new TextWriterSink(formatter, output));
    }

    private static LogEventLevel? Level(string? value) => Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level) ? level : null;

    private sealed class TextWriterSink(ITextFormatter formatter, TextWriter output) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => formatter.Format(logEvent, output);
    }
}
