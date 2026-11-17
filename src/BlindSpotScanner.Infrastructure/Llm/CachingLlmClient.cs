using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.Llm;

/// <summary>
/// Caches accepted LLM answers in <c>llm_cache</c>, keyed by a hash of (prompt name and texts,
/// model, schema, input) (doc 02), so a re-scan at the same SHA is cheap. Failed answers are never
/// cached. A cached answer is re-checked with the request's rules; one that fails them is replaced.
/// </summary>
public sealed class CachingLlmClient(BlindSpotScannerDbContext db, ILlmClient inner, PromptLibrary prompts, IOptions<LlmOptions> options) : ILlmClient
{
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var key = Key(request);
        var cached = await db.LlmCache.SingleOrDefaultAsync(e => e.Key == key, ct);
        if (cached is not null)
        {
            using var document = JsonDocument.Parse(cached.ResponseJson);
            var output = document.RootElement.Clone();
            if (request.Validate?.Invoke(output) is not { Count: > 0 })
            {
                return new LlmResult(output, [], LlmUsage.None, FromCache: true);
            }

            db.LlmCache.Remove(cached);
        }

        var result = await inner.CompleteAsync(request, ct);
        if (result.Output is not { } accepted)
        {
            await db.SaveChangesAsync(ct);
            return result;
        }

        var entry = new LlmCacheEntry { Key = key, ResponseJson = accepted.GetRawText() };
        db.LlmCache.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another scan cached the same request first; either answer is acceptable.
            db.Entry(entry).State = EntityState.Detached;
        }

        return result;
    }

    private string Key(LlmRequest request)
    {
        // The full prompt texts, not just their version: an edit to the system prompt or the embedded
        // rubric without a version bump must not serve stale answers. The unit separator keeps fields apart.
        var material = string.Join('\u001f', request.Prompt, prompts.System, prompts.Task(request.Prompt), options.Value.Model, request.OutputSchemaJson, request.InputJson);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
