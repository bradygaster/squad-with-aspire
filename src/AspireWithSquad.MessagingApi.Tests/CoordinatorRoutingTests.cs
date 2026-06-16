using AspireWithSquad.MessagingApi;

namespace AspireWithSquad.MessagingApi.Tests;

public class CoordinatorRoutingTests
{
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
        var result = CoordinatorService.DetectMentionedSquad(body);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("@IDEATION-RESEARCH-PLANNING-SQUAD uppercase", "ideation-research-planning-squad")]
    [InlineData("@Security-Hardening-Squad mixed case", "security-hardening-squad")]
    public void DetectMentionedSquad_IsCaseInsensitive(string body, string expected)
    {
        var result = CoordinatorService.DetectMentionedSquad(body);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("hello everyone")]
    [InlineData("can all squads respond?")]
    [InlineData("@ not a real mention")]
    [InlineData("@nonexistent-squad do something")]
    public void DetectMentionedSquad_WithNoMention_ReturnsNull(string body)
    {
        var result = CoordinatorService.DetectMentionedSquad(body);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("hey @ideation-research-planning-squad mid-sentence", "ideation-research-planning-squad")]
    [InlineData("see that @experience-design-squad ?", "experience-design-squad")]
    public void DetectMentionedSquad_MidSentenceMention_ReturnsSquad(string body, string expected)
    {
        var result = CoordinatorService.DetectMentionedSquad(body);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AllSquads_ContainsExpectedSquads()
    {
        Assert.Equal(7, CoordinatorService.AllSquads.Length);
        Assert.Contains("ideation-research-planning-squad", CoordinatorService.AllSquads);
        Assert.Contains("experience-design-squad", CoordinatorService.AllSquads);
        Assert.Contains("application-development-squad", CoordinatorService.AllSquads);
        Assert.Contains("azure-infrastructure-squad", CoordinatorService.AllSquads);
        Assert.Contains("quality-testing-squad", CoordinatorService.AllSquads);
        Assert.Contains("security-hardening-squad", CoordinatorService.AllSquads);
        Assert.Contains("review-deployment-squad", CoordinatorService.AllSquads);
    }
}
