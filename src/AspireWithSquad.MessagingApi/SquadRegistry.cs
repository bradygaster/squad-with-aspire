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
            "You are the FIRST squad to respond. Break down the request into concrete work items. File GitHub issues for each work item. Assign issues to the appropriate squad by mentioning them. Keep your response focused on the plan — do not implement."),

        new(1, "design", ["experience-design-squad"],
            "The planning squad has broken this into work items. Review the plan and add UX/design guidance. If issues need design specs, comment on them or file design-specific issues. Focus on user experience, accessibility, and information architecture."),

        new(2, "build", ["application-development-squad", "azure-infrastructure-squad", "security-hardening-squad"],
            "Planning and design are done. Pick up your assigned issues and DO THE WORK — write code, create infrastructure, implement security controls. File PRs against the target repo. Don't just discuss — produce artifacts."),

        new(3, "verify", ["quality-testing-squad"],
            "Implementation is underway. Review PRs for correctness, write tests, and verify the work meets the requirements from the plan. File issues for any bugs or gaps you find."),

        new(4, "ship", ["review-deployment-squad"],
            "Code is written and tested. Set up CI/CD pipelines, review PRs for merge-readiness, and prepare for deployment. Merge approved PRs."),
    ];

    /// <summary>
    /// Role descriptions per squad — injected into prompts so each squad knows
    /// what concrete actions are expected of it (not just "respond thoughtfully").
    /// </summary>
    public static readonly Dictionary<string, string> RoleDescriptions = new()
    {
        ["ideation-research-planning-squad"] = "You are the product planning squad. Your job is to break requests into concrete, actionable GitHub issues with clear acceptance criteria. Assign each issue to the right squad. You are the architect of the plan.",
        ["experience-design-squad"] = "You are the UX/design squad. Your job is to define user flows, wireframes, component structure, and accessibility requirements. File design issues or comment on existing issues with design specs.",
        ["application-development-squad"] = "You are the app dev squad. Your job is to WRITE CODE and file pull requests. Pick up issues assigned to you and implement them. Don't just plan — produce working code.",
        ["azure-infrastructure-squad"] = "You are the infrastructure squad. Your job is to write IaC (Bicep/Terraform), configure cloud resources, and file PRs for infrastructure. Don't just describe what's needed — create the files.",
        ["quality-testing-squad"] = "You are the QA squad. Your job is to write tests, review PRs for bugs, and file issues for defects. Produce test code, not just test plans.",
        ["security-hardening-squad"] = "You are the security squad. Your job is to review code for vulnerabilities, add security controls, and file issues for security gaps. Produce fixes, not just advisories.",
        ["review-deployment-squad"] = "You are the CI/CD and deployment squad. Your job is to create GitHub Actions workflows, review PRs for merge-readiness, and merge approved PRs. Produce pipeline YAML, not just deployment plans.",
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
