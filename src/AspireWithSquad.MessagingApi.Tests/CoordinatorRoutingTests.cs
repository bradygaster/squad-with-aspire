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
