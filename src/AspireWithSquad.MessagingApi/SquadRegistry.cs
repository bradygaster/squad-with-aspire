namespace AspireWithSquad.MessagingApi;

/// <summary>
/// Single source of truth for the registered squad names, phase ordering, and role descriptions.
/// Registered as a singleton and consumed by CoordinatorService and the /api/squads endpoint.
/// </summary>
public sealed class SquadRegistry
{
    public string[] Names { get; }

    /// <summary>
    /// Squads organized into execution phases. Phase 0 runs first, then phase 1 after
    /// phase 0 replies, etc. Squads within the same phase run in parallel.
    /// </summary>
    public static readonly SquadPhase[] Phases =
    [
        new(0, "plan", ["ideation-research-planning-squad"],
            "You are the FIRST squad to respond. Dispatch your research, product, and planning subagents in parallel (via `task`) to break the request down. Synthesize their outputs into a focused plan — and have your scribe subagent file the GitHub issues. Do NOT write the analysis or file the issues yourself; dispatch and synthesize."),

        new(1, "design", ["experience-design-squad"],
            "Phase 0 produced a plan. Dispatch your design subagents (via `task`) to add UX guidance, accessibility specs, and component breakdowns for each work item. Synthesize their outputs and have a subagent comment on the relevant issues. Do NOT design or comment yourself; dispatch and route the output."),

        new(2, "build", ["application-development-squad", "azure-infrastructure-squad", "security-hardening-squad"],
            "Planning and design are done. For each assigned issue, dispatch (via `task`) a subagent prompted as the right engineering persona for the work (e.g. \"You are the API architect — implement the issue described in this prompt\"). The subagent writes the code/IaC/controls AND files the PR. Your coordinator turn is for routing + synthesis only. Do NOT write code, IaC, or PRs inline."),

        new(3, "verify", ["quality-testing-squad"],
            "Implementation is underway. Dispatch your testing subagents (via `task`) — one per concern (integration, E2E, perf, regression) — to write tests for the changes and report gaps. Each subagent files its own test PRs and bug issues. You synthesize a coverage report. Do NOT write tests yourself; dispatch and report."),

        new(4, "ship", ["review-deployment-squad"],
            "Code is written and tested. Dispatch your release subagents (via `task`) — FinalReviewAgent for PR review, ProductionReadinessAgent for the deploy gate, DeploymentValidationAgent for the workflow YAML. Each subagent authors its own artifact and posts its verdict. You synthesize the merge/deploy decision. Do NOT review PRs or author YAML inline; dispatch."),
    ];

    /// <summary>
    /// Role descriptions per squad — injected into prompts so each squad knows
    /// what concrete actions are expected of it. PHRASED AS DELEGATION: each squad
    /// is the dispatcher of its team, not the doer. Direct-action verbs ("write code",
    /// "file issues") here cause the coordinator LLM to bypass the `task` tool and
    /// do the work inline — which is the anti-pattern. Keep the wording in the form
    /// "Dispatch [personas] (via `task`) to [outcome]".
    /// </summary>
    public static readonly Dictionary<string, string> RoleDescriptions = new()
    {
        ["ideation-research-planning-squad"] = "You orchestrate the product planning team. For any incoming request, dispatch (via `task`) your ResearchAgent (problem framing & user research), CompetitiveAnalysisAgent (landscape scan), ProductManagerAgent (requirements & acceptance criteria), and PlanningAgent (breakdown into work items) in parallel. Synthesize their outputs into GitHub issues. Do NOT do the research, framing, or planning yourself — your job is to dispatch the team and collate their findings.",
        ["experience-design-squad"] = "You orchestrate the UX/design team. Dispatch (via `task`) your design personas (e.g. linus for IA, livingston for accessibility, rusty for component design, saul for user flows) to produce the design artifacts. Synthesize their outputs into design specs that are filed as issues or attached to existing ones. Do NOT design yourself — dispatch and synthesize.",
        ["application-development-squad"] = "You orchestrate the app dev team. For each assigned issue, dispatch (via `task`) a general-purpose subagent prompted as the appropriate engineering persona (architect for design, implementer for code, reviewer for PR review). Your subagents write the code and file the PRs. You synthesize their work into a coherent delivery. Do NOT write code or open PRs yourself — that is the subagent's job.",
        ["azure-infrastructure-squad"] = "You orchestrate the infrastructure team. Dispatch (via `task`) AzurePlatformAgent (resource design), ContinuousIntegrationAgent (build pipelines), ContinuousDeliveryAgent (deploy pipelines), and CostGovernanceAgent (cost review) for each infra concern. Your subagents produce the Bicep/Terraform/YAML and file the PRs. You synthesize. Do NOT author IaC yourself — dispatch.",
        ["quality-testing-squad"] = "You orchestrate the QA team. Dispatch (via `task`) IntegrationTestingAgent (integration suites), PlaywrightTestingAgent (E2E), PerformanceTestingAgent (load/perf), and RegressionTestingAgent (regression coverage) per change under review. Your subagents write the tests and file the bug issues. You synthesize their coverage report. Do NOT write tests yourself — dispatch and report.",
        ["security-hardening-squad"] = "You orchestrate the security team. Dispatch (via `task`) SecurityArchitectureAgent (threat model), DependencyAnalysisAgent (SCA), IdentitySecurityAgent (authn/authz review), and ComplianceAgent (policy/compliance) per change or scope under review. Your subagents produce the findings, fixes, and security issues. You synthesize a security verdict. Do NOT review or fix inline — dispatch.",
        ["review-deployment-squad"] = "You orchestrate the release team. Dispatch (via `task`) FinalReviewAgent (merge-readiness review), ProductionReadinessAgent (deploy gate), DeploymentValidationAgent (deploy verification), and PostDeploymentVerificationAgent (smoke tests) per release candidate. Your subagents author the workflows, run the gates, and decide merge/deploy. You synthesize the go/no-go. Do NOT merge or write pipeline YAML inline — dispatch.",
    };

    public SquadRegistry(string[] names)
    {
        Names = names;
    }

    /// <summary>
    /// Returns the phase number for a given squad (0-based).
    /// Squads not in any phase get int.MaxValue (run last, if at all).
    /// </summary>
    public static int GetPhase(string squadName)
    {
        foreach (var phase in Phases)
        {
            if (phase.Squads.Contains(squadName))
                return phase.Order;
        }
        return int.MaxValue;
    }

    /// <summary>
    /// Returns the squads that should execute in a given phase.
    /// </summary>
    public static string[] GetSquadsInPhase(int phaseOrder)
    {
        var phase = Phases.FirstOrDefault(p => p.Order == phaseOrder);
        return phase?.Squads ?? [];
    }

    /// <summary>
    /// Returns the phase instruction for a given squad — tells it what's
    /// expected at this stage of the workflow.
    /// </summary>
    public static string? GetPhaseInstruction(string squadName)
    {
        foreach (var phase in Phases)
        {
            if (phase.Squads.Contains(squadName))
                return phase.Instruction;
        }
        return null;
    }

    /// <summary>
    /// Returns the role description for a squad, or null if not found.
    /// </summary>
    public static string? GetRoleDescription(string squadName)
    {
        return RoleDescriptions.GetValueOrDefault(squadName);
    }

    /// <summary>
    /// Detects if the message body contains @squad-name and returns that squad,
    /// so the coordinator can route to just that squad instead of broadcasting.
    /// </summary>
    public string? DetectMentionedSquad(string body)
    {
        foreach (var squad in Names)
        {
            if (body.Contains($"@{squad}", StringComparison.OrdinalIgnoreCase))
                return squad;
        }
        return null;
    }

    /// <summary>
    /// Detects messages that are just acknowledgments/status updates and don't need a response.
    /// Prevents infinite "acknowledged - nothing actionable" ping-pong loops.
    /// </summary>
    public static bool IsNonActionable(string body)
    {
        var lower = body.ToLowerInvariant();
        string[] markers =
        [
            "nothing actionable",
            "no action needed",
            "nothing to act on",
            "nothing to do here",
            "acknowledged",
            "just a status",
            "just a mutual",
            "no messages to send",
            "already took my turn",
            "story has moved past",
            "moved past both of us",
            "my turn is done",
            "turn is long done"
        ];
        return markers.Any(m => lower.Contains(m));
    }
}

/// <summary>
/// Represents a phase in the squad execution pipeline.
/// </summary>
public sealed record SquadPhase(int Order, string Name, string[] Squads, string Instruction);
