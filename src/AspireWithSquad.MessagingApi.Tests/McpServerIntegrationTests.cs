using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AspireWithSquad.MessagingApi.Tests;

/// <summary>
/// Integration test that verifies the MCP stdio server starts, lists tools,
/// and can execute squad_send_message against a live messaging API.
/// </summary>
public class McpServerIntegrationTests : IAsyncLifetime
{
    private McpClient? _client;
    private readonly string _extensionPath;

    public McpServerIntegrationTests()
    {
        // Resolve extension path relative to test project
        var testDir = AppContext.BaseDirectory;
        _extensionPath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..", ".github", "extensions", "squad-messaging", "extension.mjs"));
    }

    public async Task InitializeAsync()
    {
        if (!File.Exists(_extensionPath))
        {
            throw new FileNotFoundException($"MCP extension not found at: {_extensionPath}");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "node",
            Arguments = [_extensionPath],
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MESSAGING_API_URL"] = "http://localhost:59999", // intentionally wrong port for error testing
            },
        });

        _client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new() { Name = "test-client", Version = "1.0.0" },
        });
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }

    [Fact]
    public async Task McpServer_ListsTools_ReturnsThreeTools()
    {
        Assert.NotNull(_client);

        var tools = await _client.ListToolsAsync();
        var toolNames = tools.Select(t => t.Name).ToList();

        Assert.Contains("squad_send_message", toolNames);
        Assert.Contains("squad_read_recent_messages", toolNames);
        Assert.Contains("squad_read_inbox", toolNames);
        Assert.Equal(3, toolNames.Count);
    }

    [Fact]
    public async Task McpServer_SendMessage_WhenApiUnavailable_ReturnsError()
    {
        Assert.NotNull(_client);

        var result = await _client.CallToolAsync("squad_send_message", new Dictionary<string, object?>
        {
            ["from"] = "ideation-research-planning-squad",
            ["to"] = "experience-design-squad",
            ["body"] = "Hello from integration test!",
        });

        // Should get an error response (connection refused) but NOT crash
        Assert.NotNull(result);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
        Assert.True(result.IsError == true || text.Contains("Error") || text.Contains("Failed"),
            $"Expected error when API is unavailable, got: {text}");
    }

    [Fact]
    public async Task McpServer_ReadRecentMessages_WhenApiUnavailable_ReturnsError()
    {
        Assert.NotNull(_client);

        var result = await _client.CallToolAsync("squad_read_recent_messages", new Dictionary<string, object?>
        {
            ["limit"] = 5,
        });

        Assert.NotNull(result);
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
        Assert.True(result.IsError == true || text.Contains("Error") || text.Contains("Failed"),
            $"Expected error when API is unavailable, got: {text}");
    }
}
