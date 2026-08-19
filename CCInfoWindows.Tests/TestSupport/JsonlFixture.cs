using System.Text.Json;

namespace CCInfoWindows.Tests.TestSupport;

/// <summary>
/// The one statement of the Claude Code JSONL assistant-line shape the production parser is tested
/// against. Three suites used to hand-build it separately, which is why the coupling was documented in
/// a comment instead of enforced: if Claude Code renames a usage field and only one builder is
/// updated, the other suites stay green while exercising a schema that no longer exists on disk. That
/// defect class already shipped once — the <c>uniqueHash</c> dedup key made every token and cost
/// figure 4x too high.
///
/// The shape mirrors real output: a per-line <c>uuid</c> plus a <c>message.id</c> that repeats across
/// every line belonging to one assistant message, which is the identity JsonlService deduplicates on.
/// There is deliberately no <c>uniqueHash</c> key — Claude Code never writes one (0 occurrences across
/// the maintainer's 145-file live corpus), so a fixture supplying it would test a schema that does not
/// exist.
/// </summary>
internal static class JsonlFixture
{
    /// <summary>Cwd written when the caller does not name one.</summary>
    public const string DefaultCwd = "/home/user/project";

    /// <summary>Model written when the caller does not name one.</summary>
    public const string DefaultModel = "claude-sonnet-4-6";

    /// <summary>The message id a line carries when the caller does not supply one.</summary>
    public static string MessageIdFor(string uuid) => "msg_" + uuid;

    /// <summary>
    /// Serializes one assistant entry. Pass <paramref name="messageId"/> explicitly to make several
    /// lines share one message, which is how a streamed response is written. A null
    /// <paramref name="cwd"/> omits the key entirely, reproducing entries from projects where Claude
    /// Code never writes it.
    /// </summary>
    public static string AssistantLine(
        string sessionId,
        string uuid,
        string requestId,
        string? cwd = DefaultCwd,
        string? model = DefaultModel,
        long inputTokens = 0,
        long outputTokens = 0,
        long cacheCreation = 0,
        long cacheRead = 0,
        bool isSidechain = false,
        DateTimeOffset? timestamp = null,
        string? messageId = null)
    {
        var stamp = (timestamp ?? DateTimeOffset.UtcNow).ToString("O");
        var message = new
        {
            id = messageId ?? MessageIdFor(uuid),
            model,
            usage = new
            {
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                cache_read_input_tokens = cacheRead,
                cache_creation_input_tokens = cacheCreation
            }
        };

        return cwd is null
            ? JsonSerializer.Serialize(new
            {
                uuid,
                requestId,
                sessionId,
                timestamp = stamp,
                isSidechain,
                type = "assistant",
                message
            })
            : JsonSerializer.Serialize(new
            {
                uuid,
                requestId,
                sessionId,
                cwd,
                timestamp = stamp,
                isSidechain,
                type = "assistant",
                message
            });
    }

    /// <summary>
    /// Appends one line plus its terminator. File.AppendAllText closes the handle before returning, so
    /// a following File.SetLastWriteTimeUtc on the same path is safe.
    /// </summary>
    public static void AppendLine(string filePath, string line) => File.AppendAllText(filePath, line + "\n");
}
