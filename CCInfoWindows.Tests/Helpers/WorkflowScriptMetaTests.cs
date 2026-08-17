using CCInfoWindows.Helpers;

namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// Tests for the `export const meta` reader. The input is a JavaScript object literal written by a
/// user, so the cases that matter are the ones a JSON parser would choke on and the ones where a
/// naive regex would reach past the block and pull values out of unrelated code.
/// </summary>
public class WorkflowScriptMetaTests
{
    /// <summary>
    /// The real head of code-clone-review-wf_d5bf6469-814.js, verbatim: single quotes, unquoted
    /// keys, a trailing comma after the last phase, and — the trap — a JSON schema below the block
    /// whose properties are also called `title` and `description`.
    /// </summary>
    private const string RealScript = """
        export const meta = {
          name: 'code-clone-review',
          description: 'Detect Type 1/2/3 code clones across the whole repo, verify classifications, write review md',
          phases: [
            { title: 'Detect', detail: '9 detectors partitioned by file ownership' },
            { title: 'Verify', detail: 'adversarial re-read of every reported clone pair' },
            { title: 'Report', detail: 'write .planning/reviews markdown' },
          ],
        }

        const FINDINGS_SCHEMA = {
          type: 'object',
          properties: {
            title: { type: 'string', description: 'one line, names the duplicated logic' },
            severity: { type: 'string', enum: ['high', 'medium', 'low', 'nit'] },
          },
        }
        """;

    [Fact]
    public void Parse_RealScript_ReadsNameDescriptionAndPhases()
    {
        var meta = WorkflowScriptMeta.Parse(RealScript);

        Assert.Equal("code-clone-review", meta.Name);
        Assert.Equal(
            "Detect Type 1/2/3 code clones across the whole repo, verify classifications, write review md",
            meta.Description);
        Assert.Equal(["Detect", "Verify", "Report"], meta.Phases.Select(p => p.Title));
        Assert.Equal("9 detectors partitioned by file ownership", meta.Phases[0].Detail);
        Assert.Equal("write .planning/reviews markdown", meta.Phases[2].Detail);
    }

    /// <summary>
    /// The load-bearing case. Every workflow script defines JSON schemas below the meta block, and
    /// those schemas contain `title:` and `description:` keys. A search that is not bounded by the
    /// meta block's braces reports schema fields as workflow phases.
    /// </summary>
    [Fact]
    public void Parse_RealScript_IgnoresSchemaKeysBelowTheMetaBlock()
    {
        var meta = WorkflowScriptMeta.Parse(RealScript);

        Assert.Equal(3, meta.Phases.Count);
        Assert.DoesNotContain(meta.Phases, p => p.Detail == "one line, names the duplicated logic");
    }

    /// <summary>
    /// A brace inside a string must not close the block early — doing so would silently drop every
    /// phase, because the phases come after the description.
    /// </summary>
    [Fact]
    public void Parse_BraceInsideAString_DoesNotEndTheBlockEarly()
    {
        var meta = WorkflowScriptMeta.Parse("""
            export const meta = {
              name: 'fmt',
              description: 'replaces {0} and } in templates',
              phases: [{ title: 'Run', detail: 'uses } too' }],
            }
            """);

        Assert.Equal("replaces {0} and } in templates", meta.Description);
        Assert.Equal("Run", Assert.Single(meta.Phases).Title);
        Assert.Equal("uses } too", meta.Phases[0].Detail);
    }

    [Fact]
    public void Parse_SingleLineMetaBlock_IsRead()
    {
        var meta = WorkflowScriptMeta.Parse(
            "export const meta = { name: 'x', description: 'y', phases: [{ title: 'A' }] }");

        Assert.Equal("x", meta.Name);
        Assert.Equal("y", meta.Description);
        Assert.Equal("A", Assert.Single(meta.Phases).Title);
    }

    [Theory]
    [InlineData("\"double\"", "double")]
    [InlineData("'single'", "single")]
    [InlineData("`backtick`", "backtick")]
    public void Parse_AcceptsAllThreeJavaScriptQuoteStyles(string literal, string expected)
    {
        var meta = WorkflowScriptMeta.Parse($"export const meta = {{ name: {literal} }}");

        Assert.Equal(expected, meta.Name);
    }

    [Fact]
    public void Parse_ResolvesEscapedQuotesAndBackslashes()
    {
        var meta = WorkflowScriptMeta.Parse(
            @"export const meta = { name: 'it\'s here', description: 'a\\b' }");

        Assert.Equal("it's here", meta.Name);
        Assert.Equal(@"a\b", meta.Description);
    }

    /// <summary>
    /// A phase entry is allowed to be title-only. Dropping such an entry would silently shorten the
    /// table rather than showing a phase with an empty detail column.
    /// </summary>
    [Fact]
    public void Parse_PhaseWithoutADetail_IsKeptWithANullDetail()
    {
        var meta = WorkflowScriptMeta.Parse(
            "export const meta = { phases: [{ title: 'Scan' }, { title: 'Fix', detail: 'd' }] }");

        Assert.Equal(2, meta.Phases.Count);
        Assert.Null(meta.Phases[0].Detail);
        Assert.Equal("d", meta.Phases[1].Detail);
    }

    /// <summary>
    /// A phase with no title has nothing to identify it by, so it is dropped rather than rendered as
    /// a blank row.
    /// </summary>
    [Fact]
    public void Parse_PhaseWithoutATitle_IsDropped()
    {
        var meta = WorkflowScriptMeta.Parse(
            "export const meta = { phases: [{ detail: 'orphan' }, { title: 'Real' }] }");

        Assert.Equal("Real", Assert.Single(meta.Phases).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("const other = { name: 'not meta' }")]
    [InlineData("export const meta = ")]
    [InlineData("export const meta = { name: 'unterminated'")]
    public void Parse_WithoutAUsableMetaBlock_ReturnsEmpty(string script)
    {
        var meta = WorkflowScriptMeta.Parse(script);

        Assert.Null(meta.Name);
        Assert.Null(meta.Description);
        Assert.Empty(meta.Phases);
    }

    /// <summary>
    /// A computed value is not a string literal. Reading it as one would put a variable name in the
    /// tooltip.
    /// </summary>
    [Fact]
    public void Parse_NonLiteralValue_IsIgnored()
    {
        var meta = WorkflowScriptMeta.Parse("export const meta = { name: WORKFLOW_NAME, description: 'ok' }");

        Assert.Null(meta.Name);
        Assert.Equal("ok", meta.Description);
    }

    /// <summary>
    /// `phases` must not be matched as the tail of another key — the block is user-written and
    /// `subPhases` or `totalPhases` are plausible neighbours.
    /// </summary>
    [Fact]
    public void Parse_DoesNotMatchPhasesAsASuffixOfAnotherKey()
    {
        var meta = WorkflowScriptMeta.Parse("export const meta = { subPhases: [{ title: 'nope' }] }");

        Assert.Empty(meta.Phases);
    }

    /// <summary>
    /// Untrusted text: a newline inside a value would otherwise break the tooltip's line layout, and
    /// dropping it instead of replacing it would glue the surrounding words together.
    /// </summary>
    [Fact]
    public void Parse_ControlCharactersBecomeSpaces()
    {
        var meta = WorkflowScriptMeta.Parse(@"export const meta = { name: 'two\nlines' }");

        Assert.Equal("two lines", meta.Name);
    }

    // -------------------------------------------------------------------------
    // Sanitize
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Sanitize_BlankInput_ReturnsNull(string? value) =>
        Assert.Null(WorkflowScriptMeta.Sanitize(value, 60));

    [Fact]
    public void Sanitize_LongValue_IsCappedAtTheGivenLength() =>
        Assert.Equal(new string('a', 10), WorkflowScriptMeta.Sanitize(new string('a', 400), 10));

    /// <summary>
    /// Cutting between the halves of an astral character leaves a lone surrogate, which renders as a
    /// replacement box. The cap gives up one character rather than emitting one.
    /// </summary>
    [Fact]
    public void Sanitize_NeverSplitsASurrogatePair()
    {
        // "ab" + U+1F600, whose second UTF-16 unit sits exactly on a cap of 3.
        var capped = WorkflowScriptMeta.Sanitize("ab\U0001F600", 3);

        Assert.Equal("ab", capped);
        Assert.DoesNotContain(capped!, char.IsSurrogate);
    }

    // -------------------------------------------------------------------------
    // Read: every filesystem failure degrades to Empty rather than throwing
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null, "wf_x")]
    [InlineData("", "wf_x")]
    [InlineData(@"C:\does\not\exist", "wf_x")]
    [InlineData(@"C:\does\not\exist", null)]
    [InlineData(@"C:\does\not\exist", "")]
    public void Read_MissingDirectoryOrRunId_ReturnsEmptyWithoutThrowing(string? directory, string? runId)
    {
        var meta = WorkflowScriptMeta.Read(directory, runId);

        Assert.Null(meta.Name);
        Assert.Empty(meta.Phases);
    }

    [Fact]
    public void Read_ScriptsFolderPresentButNoMatchingRun_ReturnsEmpty()
    {
        using var session = new TempSession();
        session.WriteScript("other-wf_zzz.js", RealScript);

        Assert.Null(WorkflowScriptMeta.Read(session.Path, "wf_d5bf6469-814").Name);
    }

    /// <summary>
    /// The file name is "{workflowName}-{runId}.js" and the name half is unknown to the reader, so
    /// the run id is matched as a suffix.
    /// </summary>
    [Fact]
    public void Read_MatchesTheScriptByRunIdSuffix()
    {
        using var session = new TempSession();
        session.WriteScript("code-clone-review-wf_d5bf6469-814.js", RealScript);

        var meta = WorkflowScriptMeta.Read(session.Path, "wf_d5bf6469-814");

        Assert.Equal("code-clone-review", meta.Name);
        Assert.Equal(3, meta.Phases.Count);
    }

    /// <summary>
    /// The reader takes a bounded prefix of the file, so a script far larger than the meta block
    /// still costs one fixed read. Padding below the block must not change the result.
    /// </summary>
    [Fact]
    public void Read_LargeScript_StillReadsTheMetaBlock()
    {
        using var session = new TempSession();
        session.WriteScript("big-wf_1.js", RealScript + "\n" + new string('x', 500_000));

        Assert.Equal("code-clone-review", WorkflowScriptMeta.Read(session.Path, "wf_1").Name);
    }

    private sealed class TempSession : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccinfo-script-" + Guid.NewGuid().ToString("N"));

        public void WriteScript(string fileName, string content)
        {
            var scripts = System.IO.Path.Combine(Path, "workflows", "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(System.IO.Path.Combine(scripts, fileName), content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a green test over.
            }
        }
    }
}
