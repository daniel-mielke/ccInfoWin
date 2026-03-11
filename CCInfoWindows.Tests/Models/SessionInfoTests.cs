using System.Text.Json;
using CCInfoWindows.Models;

namespace CCInfoWindows.Tests.Models;

public class SessionInfoTests
{
    [Fact]
    public void IsActive_RecentActivity_ReturnsTrue()
    {
        var session = new SessionInfo
        {
            Id = "test-id",
            Cwd = "/home/user/project",
            DisplayName = "project",
            LastActivity = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var result = session.IsActive(TimeSpan.FromMinutes(30));

        Assert.True(result);
    }

    [Fact]
    public void IsActive_StaleActivity_ReturnsFalse()
    {
        var session = new SessionInfo
        {
            Id = "test-id",
            Cwd = "/home/user/project",
            DisplayName = "project",
            LastActivity = DateTimeOffset.UtcNow.AddMinutes(-60)
        };

        var result = session.IsActive(TimeSpan.FromMinutes(30));

        Assert.False(result);
    }

    [Fact]
    public void IsActive_ActivityJustWithinThreshold_ReturnsTrue()
    {
        var threshold = TimeSpan.FromMinutes(30);
        var session = new SessionInfo
        {
            Id = "test-id",
            Cwd = "/home/user/project",
            DisplayName = "project",
            // Use 1 second less than threshold to avoid timing flakiness
            LastActivity = DateTimeOffset.UtcNow - threshold + TimeSpan.FromSeconds(1)
        };

        var result = session.IsActive(threshold);

        Assert.True(result);
    }

    [Fact]
    public void JsonlEntry_DeserializesFullEntry()
    {
        const string json = """
            {
                "uuid": "abc-123",
                "requestId": "req-456",
                "sessionId": "sess-789",
                "cwd": "/home/user/project",
                "timestamp": "2024-01-15T10:30:00Z",
                "isSidechain": false,
                "agentId": "agent-001",
                "type": "assistant",
                "message": {
                    "model": "claude-sonnet-4-6",
                    "usage": {
                        "input_tokens": 1000,
                        "output_tokens": 500,
                        "cache_read_input_tokens": 200,
                        "cache_creation_input_tokens": 100
                    }
                }
            }
            """;

        var entry = JsonSerializer.Deserialize<JsonlEntry>(json, JsonlEntry.DefaultOptions);

        Assert.NotNull(entry);
        Assert.Equal("abc-123", entry.Uuid);
        Assert.Equal("req-456", entry.RequestId);
        Assert.Equal("sess-789", entry.SessionId);
        Assert.Equal("/home/user/project", entry.Cwd);
        Assert.False(entry.IsSidechain);
        Assert.Equal("agent-001", entry.AgentId);
        Assert.Equal("assistant", entry.Type);
        Assert.NotNull(entry.Message);
        Assert.Equal("claude-sonnet-4-6", entry.Message.Model);
        Assert.NotNull(entry.Message.Usage);
        Assert.Equal(1000, entry.Message.Usage.InputTokens);
        Assert.Equal(500, entry.Message.Usage.OutputTokens);
        Assert.Equal(200, entry.Message.Usage.CacheReadInputTokens);
        Assert.Equal(100, entry.Message.Usage.CacheCreationInputTokens);
    }

    [Fact]
    public void JsonlEntry_DeserializesMinimalEntry_WithoutError()
    {
        const string json = """{ "type": "summary" }""";

        var entry = JsonSerializer.Deserialize<JsonlEntry>(json, JsonlEntry.DefaultOptions);

        Assert.NotNull(entry);
        Assert.Equal("summary", entry.Type);
        Assert.Null(entry.Uuid);
        Assert.Null(entry.SessionId);
        Assert.Null(entry.Message);
        Assert.False(entry.IsSidechain);
    }

    [Fact]
    public void JsonlEntry_DeserializesEntryWithUnknownFields_WithoutError()
    {
        const string json = """
            {
                "type": "assistant",
                "unknownField1": "some value",
                "unknownField2": 42,
                "unknownNested": { "foo": "bar" }
            }
            """;

        var entry = JsonSerializer.Deserialize<JsonlEntry>(json, JsonlEntry.DefaultOptions);

        Assert.NotNull(entry);
        Assert.Equal("assistant", entry.Type);
    }
}
