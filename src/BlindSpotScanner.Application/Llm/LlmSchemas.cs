namespace BlindSpotScanner.Application.Llm;

/// <summary>The JSON Schema of each prompt's answer (from the prompt's "Output schema"), embedded from Llm/Schemas.</summary>
public static class LlmSchemas
{
    public static string Get(string prompt)
    {
        var resource = $"schemas.{prompt}.schema.json";
        using var stream = typeof(LlmSchemas).Assembly.GetManifestResourceStream(resource)
            ?? throw new KeyNotFoundException($"No answer schema for prompt '{prompt}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
