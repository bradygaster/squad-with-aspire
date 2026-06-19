using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AspireWithSquad.MessagingApi;

/// <summary>
/// Centralized telemetry for the squad orchestration system.
/// Mirrors the dispatch-insiders OTel pattern: per-agent resource attributes,
/// structured metrics (counters, histograms, gauges), and rich span trees.
/// All signals export to the Aspire dashboard via OTLP.
/// </summary>
public static class SquadTelemetry
{
    public const string ServiceName = "Squad.Orchestration";
    public const string MeterName = "Squad.Orchestration";

    // ── Tracing ──────────────────────────────────────────────────────
    public static readonly ActivitySource ActivitySource = new(ServiceName, "1.0.0");

    // ── Metrics ──────────────────────────────────────────────────────
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    // --- Agent lifecycle ---

    /// <summary>Total agent task invocations (one per squad per message).</summary>
    public static readonly Counter<long> AgentInvocations =
        Meter.CreateCounter<long>("squad.agent.invocations",
            description: "Total agent task invocations");

    /// <summary>Agent task execution time.</summary>
    public static readonly Histogram<double> AgentDuration =
        Meter.CreateHistogram<double>("squad.agent.duration_ms",
            unit: "ms", description: "Agent task execution time");

    /// <summary>Agent task failures.</summary>
    public static readonly Counter<long> AgentErrors =
        Meter.CreateCounter<long>("squad.agent.errors",
            description: "Total agent task failures");

    /// <summary>Currently running agent tasks.</summary>
    public static readonly UpDownCounter<int> AgentActive =
        Meter.CreateUpDownCounter<int>("squad.agent.active",
            description: "Currently running agent tasks");

    /// <summary>Response length from agents (chars).</summary>
    public static readonly Histogram<int> AgentResponseLength =
        Meter.CreateHistogram<int>("squad.agent.response_length",
            unit: "chars", description: "Length of agent responses in characters");

    // --- Messaging ---

    /// <summary>Total messages received (user + inter-squad).</summary>
    public static readonly Counter<long> MessagesReceived =
        Meter.CreateCounter<long>("squad.messages.received",
            description: "Total messages received by the coordinator");

    /// <summary>Total reply messages sent by squads.</summary>
    public static readonly Counter<long> MessagesSent =
        Meter.CreateCounter<long>("squad.messages.sent",
            description: "Total reply messages sent by squads");

    /// <summary>Messages dropped by the non-actionable filter.</summary>
    public static readonly Counter<long> MessagesDropped =
        Meter.CreateCounter<long>("squad.messages.dropped",
            description: "Messages dropped as non-actionable (ack-loop prevention)");

    // --- Phased dispatch ---

    /// <summary>Phase execution duration (all squads in a phase).</summary>
    public static readonly Histogram<double> PhaseDuration =
        Meter.CreateHistogram<double>("squad.phase.duration_ms",
            unit: "ms", description: "Phase execution duration");

    /// <summary>Phases completed.</summary>
    public static readonly Counter<long> PhasesCompleted =
        Meter.CreateCounter<long>("squad.phase.completed",
            description: "Total phases completed");

    // --- Knowledge ---

    /// <summary>Knowledge blocks extracted from agent responses.</summary>
    public static readonly Counter<long> KnowledgeExtractions =
        Meter.CreateCounter<long>("squad.knowledge.extractions",
            description: "Knowledge blocks extracted from agent responses");

    /// <summary>Knowledge store size after append.</summary>
    public static readonly Histogram<int> KnowledgeStoreSize =
        Meter.CreateHistogram<int>("squad.knowledge.store_size",
            unit: "chars", description: "Size of the knowledge store after append");

    /// <summary>
    /// Creates a TagList for per-agent metric attribution.
    /// Matches the dispatch-insiders pattern of 'agent.name' on every metric point.
    /// </summary>
    public static TagList AgentTags(string squadName) => new([new("agent.name", squadName)]);

    /// <summary>
    /// Creates a TagList for phase metrics.
    /// </summary>
    public static TagList PhaseTags(int phase, string phaseName) => new([
        new("phase.order", phase),
        new("phase.name", phaseName),
    ]);
}

