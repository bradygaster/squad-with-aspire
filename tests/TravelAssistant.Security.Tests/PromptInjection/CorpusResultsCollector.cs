// SEC-2b corpus results collector — honors SEC-5b CI gate contract.
//
// CONTRACT (locked with review-deployment-squad, SEC-5b prompt-injection-gate.yml):
//   Env var SEC2_CORPUS_RESULTS points to a JSON file path.
//   Shape: JSON array of { id, severity, expected, actual }.
//   Written exactly once at test-collection dispose.
//
// Wire-up for SEC-2 guard implementor:
//   1. Add to your guard xUnit Theory test class:
//        [Collection(CorpusResultsCollection.Name)]
//        public class PromptInjectionGuardTests {
//            private readonly CorpusResultsCollector _results;
//            public PromptInjectionGuardTests(CorpusResultsCollector r) => _results = r;
//
//            [Theory, Trait("Category", "PromptInjection")]
//            [MemberData(nameof(CorpusLoader.AllEntries), MemberType = typeof(CorpusLoader))]
//            public void Guard_BlocksOrAllows_PerCorpusExpectation(CorpusEntry entry) {
//                var verdict = _guard.Inspect(entry.Payload);
//                _results.Record(entry.Id, entry.Severity, entry.Expected,
//                                verdict.Blocked ? "block" : "allow");
//                Assert.Equal(entry.Expected, verdict.Blocked ? "block" : "allow");
//            }
//        }
//   2. The collection fixture flushes JSON on dispose — no per-test I/O.
//   3. If SEC2_CORPUS_RESULTS is unset (local dev), Record() is a no-op.
//
// Owner: security-hardening-squad. Do NOT change the JSON shape without
// pinging review-deployment-squad — their gate parser is shape-coupled.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace TravelAssistant.Security.Tests.PromptInjection;

public sealed class CorpusResultsCollector : IDisposable
{
    private readonly ConcurrentBag<CorpusResultRow> _rows = new();
    private readonly string? _outputPath = Environment.GetEnvironmentVariable("SEC2_CORPUS_RESULTS");
    private int _disposed;

    public void Record(string id, string severity, string expected, string actual)
    {
        if (string.IsNullOrEmpty(_outputPath)) return;
        _rows.Add(new CorpusResultRow(id, severity, expected, actual));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        if (string.IsNullOrEmpty(_outputPath)) return;

        var ordered = _rows.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        });

        var dir = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_outputPath, json);
    }
}

public sealed record CorpusResultRow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("actual")] string Actual);

[CollectionDefinition(Name)]
public sealed class CorpusResultsCollection : ICollectionFixture<CorpusResultsCollector>
{
    public const string Name = "CorpusResults";
}
