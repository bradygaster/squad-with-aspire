using AspireWithSquad.MessagingApi;

namespace AspireWithSquad.MessagingApi.Tests;

public class CoordinatorRoutingTests
{
    private static readonly SquadRegistry Registry = new([
        "ideation-research-planning-squad",
        "experience-design-squad",
        "application-development-squad",
        "azure-infrastructure-squad",
        "quality-testing-squad",
        "security-hardening-squad",
        "review-deployment-squad",
    ]);

    [Theory]
    [InlineData("@ideation-research-planning-squad please start", "ideation-research-planning-squad")]
    [InlineData("@security-hardening-squad run a scan", "security-hardening-squad")]
    [InlineData("@experience-design-squad can you review?", "experience-design-squad")]
    [InlineData("@application-development-squad build the feature", "application-development-squad")]
    [InlineData("@azure-infrastructure-squad provision resources", "azure-infrastructure-squad")]
    [InlineData("@quality-testing-squad write tests", "quality-testing-squad")]
    [InlineData("@review-deployment-squad deploy to staging", "review-deployment-squad")]
    public void DetectMentionedSquad_WithValidMention_ReturnsSquadName(string body, string expected)
    {
        var result = Registry.DetectMentionedSquad(body);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("@IDEATION-RESEARCH-PLANNING-SQUAD uppercase", "ideation-research-planning-squad")]
    [InlineData("@Security-Hardening-Squad mixed case", "security-hardening-squad")]
    public void DetectMentionedSquad_IsCaseInsensitive(string body, string expected)
    {
        var result = Registry.DetectMentionedSquad(body);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("hello everyone")]
    [InlineData("can all squads respond?")]
    [InlineData("@ not a real mention")]
    [InlineData("@nonexistent-squad do something")]
    public void DetectMentionedSquad_WithNoMention_ReturnsNull(string body)
    {
        var result = Registry.DetectMentionedSquad(body);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("hey @ideation-research-planning-squad mid-sentence", "ideation-research-planning-squad")]
    [InlineData("see that @experience-design-squad ?", "experience-design-squad")]
    public void DetectMentionedSquad_MidSentenceMention_ReturnsSquad(string body, string expected)
    {
        var result = Registry.DetectMentionedSquad(body);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Registry_ContainsExpectedSquads()
    {
        Assert.Equal(7, Registry.Names.Length);
        Assert.Contains("ideation-research-planning-squad", Registry.Names);
        Assert.Contains("experience-design-squad", Registry.Names);
        Assert.Contains("application-development-squad", Registry.Names);
        Assert.Contains("azure-infrastructure-squad", Registry.Names);
        Assert.Contains("quality-testing-squad", Registry.Names);
        Assert.Contains("security-hardening-squad", Registry.Names);
        Assert.Contains("review-deployment-squad", Registry.Names);
    }

    [Theory]
    [InlineData("Nothing actionable here", true)]
    [InlineData("Acknowledged - nothing to do", true)]
    [InlineData("My turn is done, moving on", true)]
    [InlineData("Here is the next chapter of the story", false)]
    [InlineData("Please review this code", false)]
    public void IsNonActionable_DetectsAckMessages(string body, bool expected)
    {
        Assert.Equal(expected, SquadRegistry.IsNonActionable(body));
    }
}

public class PhasedDispatchTests
{
    [Fact]
    public void Phases_CoverAllSquads()
    {
        var allSquadsInPhases = SquadRegistry.Phases
            .SelectMany(p => p.Squads)
            .OrderBy(s => s)
            .ToArray();

        var expected = new[]
        {
            "application-development-squad",
            "azure-infrastructure-squad",
            "experience-design-squad",
            "ideation-research-planning-squad",
            "quality-testing-squad",
            "review-deployment-squad",
            "security-hardening-squad",
        };

        Assert.Equal(expected, allSquadsInPhases);
    }

    [Fact]
    public void Phases_AreInCorrectOrder()
    {
        Assert.Equal(5, SquadRegistry.Phases.Length);
        Assert.Equal("plan", SquadRegistry.Phases[0].Name);
        Assert.Equal("design", SquadRegistry.Phases[1].Name);
        Assert.Equal("build", SquadRegistry.Phases[2].Name);
        Assert.Equal("verify", SquadRegistry.Phases[3].Name);
        Assert.Equal("ship", SquadRegistry.Phases[4].Name);
    }

    [Fact]
    public void Phase_Plan_OnlyContainsIdeation()
    {
        var planSquads = SquadRegistry.GetSquadsInPhase(0);
        Assert.Single(planSquads);
        Assert.Equal("ideation-research-planning-squad", planSquads[0]);
    }

    [Fact]
    public void Phase_Build_ContainsDevInfraAndSecurity()
    {
        var buildSquads = SquadRegistry.GetSquadsInPhase(2);
        Assert.Equal(3, buildSquads.Length);
        Assert.Contains("application-development-squad", buildSquads);
        Assert.Contains("azure-infrastructure-squad", buildSquads);
        Assert.Contains("security-hardening-squad", buildSquads);
    }

    [Theory]
    [InlineData("ideation-research-planning-squad", 0)]
    [InlineData("experience-design-squad", 1)]
    [InlineData("application-development-squad", 2)]
    [InlineData("azure-infrastructure-squad", 2)]
    [InlineData("security-hardening-squad", 2)]
    [InlineData("quality-testing-squad", 3)]
    [InlineData("review-deployment-squad", 4)]
    public void GetPhase_ReturnsCorrectPhaseForSquad(string squad, int expectedPhase)
    {
        Assert.Equal(expectedPhase, SquadRegistry.GetPhase(squad));
    }

    [Fact]
    public void GetPhase_UnknownSquad_ReturnsMaxValue()
    {
        Assert.Equal(int.MaxValue, SquadRegistry.GetPhase("nonexistent-squad"));
    }

    [Fact]
    public void IdeationRunsBeforeDesign_DesignBeforeBuild()
    {
        var ideationPhase = SquadRegistry.GetPhase("ideation-research-planning-squad");
        var designPhase = SquadRegistry.GetPhase("experience-design-squad");
        var devPhase = SquadRegistry.GetPhase("application-development-squad");
        var qaPhase = SquadRegistry.GetPhase("quality-testing-squad");
        var deployPhase = SquadRegistry.GetPhase("review-deployment-squad");

        Assert.True(ideationPhase < designPhase, "Ideation must run before design");
        Assert.True(designPhase < devPhase, "Design must run before build");
        Assert.True(devPhase < qaPhase, "Build must run before verify");
        Assert.True(qaPhase < deployPhase, "Verify must run before ship");
    }

    [Fact]
    public void NoSquadAppearsInMultiplePhases()
    {
        var allSquads = SquadRegistry.Phases.SelectMany(p => p.Squads).ToList();
        var distinct = allSquads.Distinct().ToList();
        Assert.Equal(distinct.Count, allSquads.Count);
    }
}

public class RoleDescriptionTests
{
    [Fact]
    public void AllSquadsHaveRoleDescriptions()
    {
        var registry = new SquadRegistry([
            "ideation-research-planning-squad",
            "experience-design-squad",
            "application-development-squad",
            "azure-infrastructure-squad",
            "quality-testing-squad",
            "security-hardening-squad",
            "review-deployment-squad",
        ]);

        foreach (var squad in registry.Names)
        {
            var role = SquadRegistry.GetRoleDescription(squad);
            Assert.NotNull(role);
            Assert.NotEmpty(role);
        }
    }

    [Fact]
    public void AllSquadsHavePhaseInstructions()
    {
        var registry = new SquadRegistry([
            "ideation-research-planning-squad",
            "experience-design-squad",
            "application-development-squad",
            "azure-infrastructure-squad",
            "quality-testing-squad",
            "security-hardening-squad",
            "review-deployment-squad",
        ]);

        foreach (var squad in registry.Names)
        {
            var instruction = SquadRegistry.GetPhaseInstruction(squad);
            Assert.NotNull(instruction);
            Assert.NotEmpty(instruction);
        }
    }

    [Fact]
    public void AppDevRole_MentionsWritingCode()
    {
        var role = SquadRegistry.GetRoleDescription("application-development-squad");
        Assert.Contains("code", role!);
        Assert.Contains("PRs", role!);
    }

    [Fact]
    public void IdeationRole_MentionsFilingIssues()
    {
        var role = SquadRegistry.GetRoleDescription("ideation-research-planning-squad");
        Assert.Contains("issues", role!);
        Assert.Contains("plan", role!);
    }

    [Fact]
    public void QARole_MentionsWritingTests()
    {
        var role = SquadRegistry.GetRoleDescription("quality-testing-squad");
        Assert.Contains("tests", role!);
        Assert.Contains("bug", role!);
    }
}

public class KnowledgeExtractionTests
{
    [Fact]
    public void ExtractKnowledge_WithBlock_SeparatesKnowledgeFromReply()
    {
        var input = "Here is my reply.\n<knowledge>Learned that user wants React frontend.</knowledge>\nExtra text.";
        var (visible, knowledge) = CoordinatorService.ExtractKnowledge(input);

        Assert.Equal("Learned that user wants React frontend.", knowledge);
        Assert.DoesNotContain("<knowledge>", visible);
        Assert.Contains("Here is my reply.", visible);
    }

    [Fact]
    public void ExtractKnowledge_WithoutBlock_ReturnsOriginalText()
    {
        var input = "Just a normal reply with no knowledge.";
        var (visible, knowledge) = CoordinatorService.ExtractKnowledge(input);

        Assert.Equal(input, visible);
        Assert.Null(knowledge);
    }

    [Fact]
    public void ExtractKnowledge_MultilineBlock_ExtractsAll()
    {
        var input = "Reply.\n<knowledge>Line 1.\nLine 2.\nLine 3.</knowledge>";
        var (visible, knowledge) = CoordinatorService.ExtractKnowledge(input);

        Assert.Contains("Line 1.", knowledge);
        Assert.Contains("Line 3.", knowledge);
        Assert.DoesNotContain("<knowledge>", visible);
    }
}

public class TurnBudgetTests
{
    [Fact]
    public void MaxTurnsPerSquad_IsReasonableDefault()
    {
        Assert.InRange(CoordinatorService.MaxTurnsPerSquad, 3, 10);
    }

    [Fact]
    public void MaxTurnsPerSquad_IsFive()
    {
        Assert.Equal(5, CoordinatorService.MaxTurnsPerSquad);
    }

    [Theory]
    [InlineData(0, "FINAL TURN")]
    [InlineData(1, "1 turn remaining")]
    [InlineData(3, "4 turns remaining")]
    public void BudgetPrompt_ContainsUrgencyAtLowBudget(int turnsRemaining, string expectedFragment)
    {
        var budgetBlock = turnsRemaining switch
        {
            0 => "⚠️ FINAL TURN: This is your LAST action. You MUST produce a concrete deliverable NOW — file an issue, submit a PR, write a spec, or create a test. No planning, no \"next steps\". Ship something.",
            1 => "⏰ BUDGET: 1 turn remaining after this one. Wrap up — produce your final artifact on the next turn.",
            _ => $"BUDGET: You have {turnsRemaining + 1} turns remaining (of {CoordinatorService.MaxTurnsPerSquad} total). Each turn must produce forward progress. Don't deliberate — act."
        };

        Assert.Contains(expectedFragment, budgetBlock);
    }

    [Fact]
    public void BudgetPrompt_FinalTurn_DemandsConcrete()
    {
        var budgetBlock = "⚠️ FINAL TURN: This is your LAST action. You MUST produce a concrete deliverable NOW — file an issue, submit a PR, write a spec, or create a test. No planning, no \"next steps\". Ship something.";
        Assert.Contains("MUST", budgetBlock);
        Assert.Contains("file an issue", budgetBlock);
        Assert.Contains("Ship something", budgetBlock);
    }
}
