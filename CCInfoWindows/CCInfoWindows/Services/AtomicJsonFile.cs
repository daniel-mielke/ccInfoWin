using System.Text.Json;
using CCInfoWindows.Helpers;

namespace CCInfoWindows.Services;

/// <summary>
/// The single implementation of the JSON-store durability pattern every persisted-state service in
/// this app shares: "read the file or fall back" on load, and "serialize to &lt;file&gt;.tmp, publish
/// with File.Move(overwrite: true)" on save.
///
/// Why it exists: the rule used to live in five hand-copied writer bodies and five hand-copied
/// loaders, and had already drifted — one store wrote without any lock, another skipped the rename
/// entirely. Hardening (an fsync, a delete retry, a narrower catch) now has exactly one place to land.
///
/// Locking is deliberately NOT part of this helper. Each store owns its own policy — a SemaphoreSlim
/// with a bounded wait, a Lock gate, or none at all — and holds it around the call.
/// </summary>
internal static class AtomicJsonFile
{
    /// <summary>Suffix of the staging file a write is published from.</summary>
    internal const string TempFileSuffix = ".tmp";

    /// <summary>Staging path for <paramref name="filePath"/>; a leftover one is never read back.</summary>
    internal static string TempPathFor(string filePath) => filePath + TempFileSuffix;

    /// <summary>
    /// Deserializes <paramref name="filePath"/>, or returns null when the file is missing, holds a
    /// JSON null, or cannot be read. A missing file is the normal first-run state and is not logged;
    /// everything else reaches <see cref="AppLog"/> under <paramref name="logSource"/>. Callers that
    /// want a default instead of null coalesce at the call site.
    /// </summary>
    internal static T? Read<T>(
        string filePath, JsonSerializerOptions options, string logSource, string failureMessage)
        where T : class
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (Exception ex)
        {
            AppLog.Write(logSource, ex, failureMessage);
            return null;
        }
    }

    /// <summary>
    /// Serializes <paramref name="value"/> and publishes it atomically: File.WriteAllText truncates
    /// before writing, so writing the target in place would leave a half-written file behind on an
    /// interruption — which every loader here turns into silent defaults. The staging file is removed
    /// again when the write fails.
    ///
    /// A caller that caches "what is on disk" must assign that cache only on a true return, which is
    /// the crash-safety invariant this helper exists to keep in one place.
    /// </summary>
    /// <param name="viaTempFile">
    /// Opt-out of the tmp+rename dance for a store whose payload is a handful of independent flags,
    /// where a torn file costs at most one re-fired toast and the extra file operations are not worth
    /// it (NotificationStateStore). Never turn this off for a payload a loader reads back as a whole.
    /// </param>
    /// <returns>true when the payload reached <paramref name="filePath"/>.</returns>
    internal static bool Write<T>(
        string filePath,
        T value,
        JsonSerializerOptions options,
        string logSource,
        string failureMessage,
        bool viaTempFile = true)
    {
        var writePath = viaTempFile ? TempPathFor(filePath) : filePath;
        try
        {
            EnsureDirectory(filePath);
            File.WriteAllText(writePath, JsonSerializer.Serialize(value, options));
            if (viaTempFile) File.Move(writePath, filePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(logSource, ex, failureMessage);
            if (viaTempFile) DiscardTemp(filePath, logSource);
            return false;
        }
    }

    /// <summary>
    /// Async twin of <see cref="Write{T}"/>. ConfigureAwait(false) throughout: the sync callers may be
    /// blocking the dispatcher on their own write lock, so a continuation captured onto it deadlocks.
    /// A cancellation is not a failure — it propagates instead of being logged as one.
    /// </summary>
    internal static async Task<bool> WriteAsync<T>(
        string filePath,
        T value,
        JsonSerializerOptions options,
        string logSource,
        string failureMessage,
        CancellationToken ct = default)
    {
        var tempPath = TempPathFor(filePath);
        try
        {
            EnsureDirectory(filePath);
            var json = JsonSerializer.Serialize(value, options);
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write(logSource, ex, failureMessage);
            DiscardTemp(filePath, logSource);
            return false;
        }
    }

    /// <summary>
    /// Best-effort removal of the staging file: a failure between the tmp write and the move would
    /// otherwise leave the fragment in %LOCALAPPDATA% forever.
    /// </summary>
    internal static void DiscardTemp(string filePath, string logSource)
    {
        try
        {
            var tempPath = TempPathFor(filePath);
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            AppLog.Write(logSource, ex, "stale temp file left behind");
        }
    }

    // The stores build their file path as Path.Combine(directory, fileName), so the parent is the
    // store's own directory. An empty result throws out of CreateDirectory and is then reported like
    // any other write failure, which is what the hand-written bodies did too.
    private static void EnsureDirectory(string filePath) =>
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? string.Empty);
}
