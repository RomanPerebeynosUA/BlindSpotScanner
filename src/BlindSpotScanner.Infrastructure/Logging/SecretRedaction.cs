using System.Collections;
using System.Reflection;
using BlindSpotScanner.Infrastructure.Llm;
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
        services.AddSerilog((_, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .Destructure.With<RedactingDestructuringPolicy>()
            .Enrich.FromLogContext()
            .WriteTo.Console(new ScrubbingFormatter(new CompactJsonFormatter())));
}
