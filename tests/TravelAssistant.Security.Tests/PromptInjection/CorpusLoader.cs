// SEC-2b · Shared corpus loader for prompt-injection test corpus v1.x.
//
// Owner: security-hardening-squad
// Consumers: TravelAssistant.Security.Tests (SEC-2 guard), tests/llm-eval (output-layer eval).
//
// Contract:
//   - Source of truth: tests/TravelAssistant.Security.Tests/PromptInjection/Corpus/injection-corpus.yaml
//   - DTOs use IgnoreUnmatchedProperties so additive YAML changes (new fields, new vectors)
//     are NON-BREAKING for downstream consumers.
//   - To identify benign payloads, use IsBenign(entry) — do NOT branch on `expected:` or `id` prefix
//     directly. Today both signals exist (`category: benign` + `b-*` id prefix), but the
//     CANONICAL signal is `category: benign`. Future versions may add benign payloads with
//     non-`b-*` ids (e.g., regression captures); the category field will remain stable.
//
// Adding a new field to the YAML?
//   1. Add an [YamlMember] property to CorpusEntry.
//   2. Bump the `version:` string in the YAML.
//   3. Ping consumers (review-deployment for CI gate, quality-testing for eval) if the
//      new field changes the meaning of an existing field. Pure additions are safe.
//
// Do NOT change the shape of CorpusEntry public surface without coordinating with
// quality-testing-squad and review-deployment-squad. The CI gate JSON contract
// (CorpusResultsCollector.cs) and the QA eval (tests/llm-eval/CorpusEchoTests.cs)
// both depend on the field names below.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TravelAssistant.Security.Tests.PromptInjection;

/// <summary>
/// One adversarial or benign payload in the SEC-2b corpus.
/// </summary>
public sealed class CorpusEntry
{
    /// <summary>Stable identifier (e.g., "d-1", "i-3", "b-2"). Used as the test name.</summary>
    public string Id { get; set; } = "";

    /// <summary>Taxonomy bucket (instruction-override, indirect-html, unicode-tag, benign, ...).</summary>
    public string Category { get; set; } = "";

    /// <summary>critical | high | medium</summary>
    public string Severity { get; set; } = "";

    /// <summary>direct | indirect | unicode | encoded | multimodal-stub</summary>
    public string Vector { get; set; } = "";

    /// <summary>The literal input the guard / model sees.</summary>
    public string Payload { get; set; } = "";

    /// <summary>block | flag | sanitize. For benign payloads this is "sanitize" (pass-through).</summary>
    public string Expected { get; set; } = "";

    /// <summary>Provenance / why this matters. Free text.</summary>
    public string Notes { get; set; } = "";
}

/// <summary>
/// Top-level corpus document.
/// </summary>
public sealed class CorpusDocument
{
    public string Version { get; set; } = "";

    [YamlMember(Alias = "generated_utc")]
    public string GeneratedUtc { get; set; } = "";

    public List<CorpusEntry> Payloads { get; set; } = new();
}

/// <summary>
/// Loads and queries the SEC-2b prompt-injection corpus.
/// </summary>
public static class CorpusLoader
{
    /// <summary>
    /// The canonical category value used to mark benign control payloads.
    /// </summary>
    public const string BenignCategory = "benign";

    /// <summary>
    /// Load the corpus from a file path. Throws if the file is missing or malformed.
    /// </summary>
    public static CorpusDocument LoadFromFile(string yamlPath)
    {
        if (!File.Exists(yamlPath))
        {
            throw new FileNotFoundException(
                $"SEC-2b corpus file not found at '{yamlPath}'. " +
                "Ensure the corpus YAML is linked into the test project (Copy=PreserveNewest).",
                yamlPath);
        }

        var text = File.ReadAllText(yamlPath);
        return LoadFromString(text);
    }

    /// <summary>
    /// Load the corpus from a YAML string.
    /// </summary>
    public static CorpusDocument LoadFromString(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var doc = deserializer.Deserialize<CorpusDocument>(yaml)
                  ?? throw new InvalidDataException("Corpus YAML deserialized to null.");
        return doc;
    }

    /// <summary>
    /// True if this entry is a benign control payload.
    /// Canonical signal is <c>category == "benign"</c>. The <c>b-*</c> id prefix is a
    /// secondary convention and may not hold for all future entries.
    /// </summary>
    public static bool IsBenign(CorpusEntry e) =>
        string.Equals(e.Category, BenignCategory, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>All benign control payloads.</summary>
    public static IEnumerable<CorpusEntry> Benign(this CorpusDocument doc) =>
        doc.Payloads.Where(IsBenign);

    /// <summary>All adversarial payloads (everything that is not benign).</summary>
    public static IEnumerable<CorpusEntry> Adversarial(this CorpusDocument doc) =>
        doc.Payloads.Where(p => !IsBenign(p));

    /// <summary>Adversarial payloads at a given severity (case-insensitive).</summary>
    public static IEnumerable<CorpusEntry> AdversarialBySeverity(this CorpusDocument doc, string severity) =>
        doc.Adversarial().Where(p =>
            string.Equals(p.Severity, severity, System.StringComparison.OrdinalIgnoreCase));
}
