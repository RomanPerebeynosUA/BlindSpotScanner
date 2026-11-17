using System.Text;
using System.Text.Json;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Domain.Entities;

namespace BlindSpotScanner.Application.Scanning;

/// <param name="Notes">What the phase could not do, in plain words (they feed ScanStep notes and unverified reasons).</param>
public sealed record PhaseResult<T>(T Value, LlmUsage Usage, IReadOnlyList<string> Notes);

/// <summary>
/// P4, LLM part (doc 05, prompts/classify-tests.md): each file the profile rules left Ambiguous is
/// classified by the LLM. A file that can't be sent or isn't answered stays Ambiguous, never guessed.
/// </summary>
public sealed class ClassifyAmbiguousPhase(ILlmClient llm, IBlobReader blobs)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PhaseResult<TestClassification>> ClassifyAsync(RunContext run, TestClassification rules, RepoProfile profile, int maxFileBytes, CancellationToken ct)
    {
        var files = new List<ClassifiedTestFile>();
        var notes = new List<string>();
        var usage = LlmUsage.None;
        foreach (var file in rules.Files)
        {
            if (file.Kind != TestFileKind.Ambiguous)
            {
                files.Add(file);
                continue;
            }

            var (decided, note, cost) = await ClassifyOneAsync(run, file, profile, maxFileBytes, ct);
            files.Add(decided);
            usage = usage.Add(cost);
            if (note is not null)
            {
                notes.Add(note);
            }
        }

        return new PhaseResult<TestClassification>(new TestClassification(files), usage, notes);
    }

    private async Task<(ClassifiedTestFile File, string? Note, LlmUsage Usage)> ClassifyOneAsync(RunContext run, ClassifiedTestFile file, RepoProfile profile, int maxFileBytes, CancellationToken ct)
    {
        if (LlmContentPolicy.IsDenied(file.Path))
        {
            return (file, $"{file.Path}: not sent to the LLM (denied path)", LlmUsage.None);
        }

        var content = await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct);
        if (Encoding.UTF8.GetByteCount(content) > maxFileBytes)
        {
            return (file, $"{file.Path}: not sent to the LLM (larger than {maxFileBytes} bytes)", LlmUsage.None);
        }

        var input = JsonSerializer.Serialize(new { profileMarkdown = profile.Markdown, files = new[] { new { path = file.Path, content } } }, Json);
        var result = await llm.CompleteAsync(new LlmRequest(Prompts.ClassifyTests, input, LlmSchemas.Get(Prompts.ClassifyTests), answer => Check(answer, file.Path)), ct);
        if (result.Output is not { } output)
        {
            return (file, $"{file.Path}: classification failed ({result.Errors.FirstOrDefault() ?? "no answer"})", result.Usage);
        }

        var answer = output.GetProperty("files")[0];
        var kind = answer.GetProperty("kind").GetString() == "integration" ? TestFileKind.Integration : TestFileKind.Other;
        var reason = $"LLM ({answer.GetProperty("confidence").GetString()}): {answer.GetProperty("reason").GetString()}";
        return (file with { Kind = kind, Reason = reason }, null, result.Usage);
    }

    private static IReadOnlyList<string> Check(JsonElement answer, string path)
    {
        var files = answer.GetProperty("files");
        return files.GetArrayLength() == 1 && files[0].GetProperty("path").GetString() == path
            ? []
            : [$"files: expected exactly one answer for '{path}'"];
    }
}
