using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AspireWithSquad.MessagingApi.Tests;

/// <summary>
/// Integration test that verifies the MCP stdio server starts, lists tools,
/// and can execute squad_send_message against a live messaging API.
/// </summary>
/// <remarks>
/// These tests are currently disabled in CI because <c>squad-messaging/extension.mjs</c>
/// imports <c>joinSession()</c> from <c>@github/copilot-sdk/extension</c>, which only works
/// when the extension is launched as a child process of the Copilot CLI. The Node process
/// spawned by <see cref="StdioClientTransport"/> in <see cref="InitializeAsync"/> aborts
/// immediately with <c>joinSession() is intended for extensions running as child processes
/// of the Copilot CLI.</c> Re-enable when the extension is refactored to support a standalone
/// MCP transport (or when the test harness wraps it in a Copilot-CLI shim).
/// </remarks>
public class McpServerIntegrationTests : IAsyncLifetime
{
    private const string SkipReason = "Extension requires Copilot CLI host context (calls joinSession()); cannot run standalone via StdioClientTransport.";

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
            // Not fatal — all tests in this fixture are currently skipped.
            return;
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "node",
            Arguments = [_extensionPath],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["MESSAGING_API_URL"] = "http://localhost:59999", // intentionally wrong port for error testing
            },
        });

        try
        {
            _client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new() { Name = "test-client", Version = "1.0.0" },
            });
        }
        catch
        {
            // Swallow — fixture continues to set up, all Facts are skipped.
            // See class-level remarks for context.
        }
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }

    [Fact(Skip = SkipReason)]
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

    [Fact(Skip = SkipReason)]
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

    [Fact(Skip = SkipReason)]
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
