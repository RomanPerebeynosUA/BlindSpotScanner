namespace BlindSpotScanner.Infrastructure.Llm;

/// <summary>The <c>Llm</c> configuration section (doc 10, D20). The API key comes from user secrets or ANTHROPIC_API_KEY.</summary>
public sealed class LlmOptions
{
    public const string WorkspaceHeader = "anthropic-workspace-id";

    public string Model { get; set; } = "claude-sonnet-5-5";

    public int MaxTokensPerCall { get; set; } = 8000;

    /// <summary>The default cost cap of one scan (a scan request may set its own).</summary>
    public decimal MaxCostUsdPerScan { get; set; } = 5.0m;

    public string PromptSetVersion { get; set; } = "1.0.0";

    public decimal InputUsdPerMTok { get; set; } = 2.00m;

    public decimal OutputUsdPerMTok { get; set; } = 10.00m;

    public string? ApiKey { get; set; }

    /// <summary>
    /// The Claude Console workspace to bill (<c>wrkspc_…</c>), sent as <c>anthropic-workspace-id</c>. Needed
    /// for an organization API key that isn't scoped to one workspace (D28); null sends no header.
    /// </summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Null uses the SDK default; tests point it at WireMock.</summary>
    public string? BaseUrl { get; set; }
}
