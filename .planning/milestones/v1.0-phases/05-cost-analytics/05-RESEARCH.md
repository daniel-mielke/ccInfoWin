# Phase 5: Cost Analytics - Research

**Researched:** 2026-03-16
**Domain:** Cost calculation from JSONL + LiteLLM pricing API + WinUI 3 segmented control + shimmer animation
**Confidence:** HIGH

## Summary

Phase 5 adds cost tracking and token aggregation by time period on top of the existing JSONL pipeline. The core work is threefold: (1) extend `JsonlService` and `IJsonlService` to support time-period aggregation (session / today / week / month) across all sessions including subagents, (2) build a `LiteLLMPricingService` that fetches the pricing JSON from GitHub raw, caches it locally for 12 hours, and computes USD cost per entry using `costUSD` with token-based fallback, and (3) wire a new STATISTIKEN section into the UI with a `CommunityToolkit.WinUI.Controls.Segmented` tab bar plus shimmer placeholders during background aggregation.

The key structural decision: `costUSD` is a **top-level field** in each JSONL assistant entry (confirmed by ccusage open-source tool). Some entries lack it (model not found in pricing DB, or older Claude Code version). The fallback formula is `input * input_price + output * output_price + cache_creation * cache_creation_price + cache_read * cache_read_price`. For 1M-context models (Opus 4.6, Sonnet 4.5 extended), LiteLLM uses `_above_200k_tokens` suffix keys for the higher tier. Entries where the model is not in the pricing DB get tilde prefix on the cost display.

**Primary recommendation:** Add `costUSD` field to `JsonlEntry`, extend `TokenSummary` into `StatisticsSummary` with full token + cost data, add `GetStatistics(TimePeriod)` to `IJsonlService`, build `IPricingService` + `LiteLLMPricingService` as a singleton, use `CommunityToolkit.WinUI.Controls.Segmented` v8.2.251219 for the tab bar, and implement shimmer via a `Storyboard`/`ColorAnimation` on placeholder `Border` elements.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Tab-Bar (Time Period Switcher)**
- Segmented control style — connected bar with 4 segments, matching macOS original
- Labels fully written out: Session / Heute / Woche / Monat (not abbreviated)
- Active segment highlighted with filled background, inactive segments use muted text color
- Default tab on startup: "Session" (consistent with session dropdown above, no aggregation needed)
- Tab state is not persisted — always starts on "Session"
- Switching tabs triggers re-aggregation of token/cost data for selected time period
- Subagent tokens included in all time period aggregations (TOKS-03)

**Tab-Bar Loading State**
- Shimmer placeholders on value positions during aggregation (no layout jump)
- Shimmer shown when switching to Heute/Woche/Monat tabs that require cross-session aggregation
- Session tab loads instantly from cache (no shimmer needed)

**Statistics Data Table**
- Key-value table below tab bar (per styleguide section 9.2)
- Rows: Eingabe, Ausgabe, Cache-Schreiben, Cache-Lesen, **Gesamt** (bold), **Kosten** (bold)
- Labels 13px Regular gray, values 13px Medium white, bold rows 13px Semibold
- Cost value prefixed with ~ (tilde) when model not in pricing database (COST-03)
- Burn rate displayed below cost row

### Claude's Discretion
- Cost calculation implementation (costUSD primary, token*price fallback logic)
- LiteLLM API integration details (endpoint, response parsing, 12h cache strategy)
- Tiered pricing implementation for 1M-context models (COST-04)
- Burn rate calculation algorithm (time window, smoothing, unit choice)
- Burn rate visualization (number format, trend indicator)
- Shimmer animation implementation (WinUI 3 approach)
- JSONL deduplication by messageId and requestId (TOKS-04)
- Fallback/bundled prices when LiteLLM API is unreachable
- Settings UI for pricing data source and last fetch time (COST-06)
- JsonlEntry model extension to include costUSD field

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TOKS-02 | Tab bar (segmented control) switches between four time periods with loading indicator | CommunityToolkit.WinUI.Controls.Segmented v8.2.251219 confirmed; shimmer via Storyboard |
| TOKS-03 | Subagent tokens included in all time period aggregations | Subagent JSONL files already parsed in JsonlService; extend GetStatistics to include subagent files |
| TOKS-04 | JSONL entries deduplicated by messageId and requestId | `SeenIds` HashSet already exists in ProjectData; pattern extends naturally to time-period aggregation |
| COST-01 | Model prices fetched live from LiteLLM Pricing API with 12-hour cache | URL confirmed: https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json |
| COST-02 | Costs primarily from costUSD field in JSONL; fallback to token count * model price | costUSD is top-level field in JSONL entry; confirmed by ccusage source analysis |
| COST-03 | Estimated costs marked with tilde prefix (~) when model not in pricing database | Logic: if fallback was used, set IsEstimated flag; format cost as "~$X.XX" |
| COST-04 | Tiered pricing applied for 1M-context models (higher input price above 200K tokens) | LiteLLM uses `input_cost_per_token_above_200k_tokens` key; Claude Opus 4.6 confirmed as 1M-context model |
| COST-05 | Burn rate (token consumption speed) calculated and displayed | Calculate from timestamps of JSONL entries within a rolling window |
| COST-06 | Settings show pricing data source (live API or fallback) and last fetch time | Extend AppSettings with PricingSource + LastPricingFetch; display in SettingsView |
| DATA-05 | LiteLLM pricing cache persisted locally with fallback to bundled prices | Save JSON to %LOCALAPPDATA%\CCInfoWindows\litellm-pricing-cache.json; embed fallback as resource |
</phase_requirements>

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| CommunityToolkit.WinUI.Controls.Segmented | 8.2.251219 | Tab bar / segmented control UI | Official WinUI 3 community toolkit component; macOS-style pill behavior built in |
| System.Text.Json | Built-in .NET 9 | Parse LiteLLM pricing JSON and JSONL | Already used throughout project |
| HttpClient (singleton) | Built-in .NET 9 | Fetch LiteLLM pricing JSON from GitHub raw | Already registered as singleton in DI |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| CommunityToolkit.Mvvm | 8.4 | [ObservableProperty] for SelectedTabIndex, IsAggregating | Already in project |
| Microsoft.UI.Xaml.Media.Animation.Storyboard | WinUI 3 / Windows App SDK 1.8 | Shimmer animation via ColorAnimation on Border.Background | WinUI 3 native animation for skeleton loading |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| CommunityToolkit.Segmented | Custom RadioButton grid | Toolkit version handles keyboard nav, accessibility, theming automatically |
| CommunityToolkit.Segmented | Syncfusion SegmentedControl | Syncfusion requires commercial license; toolkit is MIT |
| Storyboard shimmer | 3rd-party skeleton library | No relevant WinUI 3 skeleton library exists; Storyboard approach is standard for this platform |
| GitHub raw URL | LiteLLM hosted API | GitHub raw is stable, version-pinnable; no auth required |

**Installation:**
```bash
dotnet add package CommunityToolkit.WinUI.Controls.Segmented --version 8.2.251219
```

Note: `CommunityToolkit.WinUI.Controls.Segmented` requires the `CommunityToolkit.WinUI.Extensions` dependency (auto-resolved by NuGet).

---

## Architecture Patterns

### Recommended Project Structure (new additions)

```
CCInfoWindows/CCInfoWindows/
  Models/
    StatisticsSummary.cs      # Extended token + cost aggregation record
    PricingData.cs            # LiteLLM pricing model (parsed JSON)
    TimePeriod.cs             # Enum: Session, Today, Week, Month
  Services/
    Interfaces/
      IPricingService.cs      # Contract: GetPriceAsync, PricingSource, LastFetch
    LiteLLMPricingService.cs  # Fetches, caches, and queries LiteLLM pricing JSON
  Helpers/
    CostFormatter.cs          # FormatCost(decimal, bool isEstimated) -> "$X.XX" or "~$X.XX"
    BurnRateCalculator.cs     # ComputeBurnRate(entries, windowMinutes) -> tokens/hour
  Resources/
    fallback-prices.json      # Bundled price data for offline fallback (embedded resource)
```

### Pattern 1: TimePeriod Enum + Overloaded IJsonlService

**What:** Add `TimePeriod` enum and `GetStatistics(TimePeriod period)` method to `IJsonlService`. For Session: use existing per-project data. For Today/Week/Month: scan all projects and filter entries by `Timestamp`.

**When to use:** Avoids a separate aggregation service; keeps all JSONL aggregation in one place.

```csharp
// Source: project pattern established in Phase 4
public enum TimePeriod { Session, Today, Week, Month }

public interface IJsonlService
{
    // ... existing members ...
    StatisticsSummary GetStatistics(TimePeriod period, string? sessionId = null);
}
```

### Pattern 2: IPricingService with 12h Cache

**What:** `LiteLLMPricingService` is a singleton. On first call (or after 12h), fetches from GitHub raw URL, parses into a `Dictionary<string, ModelPricing>` keyed by model name, persists to local JSON. On failure, loads from bundled fallback.

**When to use:** Stateful cache that survives across tab switches.

```csharp
// Source: established ClaudeApiService pattern in project
public interface IPricingService
{
    /// <summary>Returns pricing for a model, or null if not found.</summary>
    ModelPricing? GetPrice(string modelName);
    PricingSource Source { get; }
    DateTimeOffset? LastFetch { get; }
    Task EnsurePricesLoadedAsync();
}

public enum PricingSource { Live, Fallback, Unknown }
```

### Pattern 3: Cost Calculation Logic

**What:** For each JSONL entry, use `costUSD` if present and > 0. Otherwise compute from token counts and model pricing. If model not in pricing DB, set `IsEstimated = true`.

```csharp
// Source: confirmed from ccusage documentation
private static (decimal cost, bool isEstimated) ComputeCost(
    JsonlEntry entry, ModelPricing? pricing)
{
    if (entry.CostUsd is > 0m)
        return (entry.CostUsd.Value, false);

    if (pricing is null)
        return (0m, true); // unknown model — mark estimated

    var usage = entry.Message?.Usage;
    if (usage is null)
        return (0m, false);

    var inputTokens = usage.InputTokens ?? 0;
    var outputTokens = usage.OutputTokens ?? 0;
    var cacheCreation = usage.CacheCreationInputTokens ?? 0;
    var cacheRead = usage.CacheReadInputTokens ?? 0;

    // Tiered pricing for 1M-context models (COST-04)
    var inputPrice = inputTokens > TierBreakpointTokens && pricing.InputCostAbove200k.HasValue
        ? pricing.InputCostAbove200k.Value
        : pricing.InputCostPerToken;

    var cost = (inputTokens * inputPrice)
             + (outputTokens * pricing.OutputCostPerToken)
             + (cacheCreation * pricing.CacheCreationCost)
             + (cacheRead * pricing.CacheReadCost);

    return ((decimal)cost, false);
}

private const long TierBreakpointTokens = 200_000;
```

### Pattern 4: LiteLLM Model Key Lookup

**What:** The LiteLLM JSON is keyed by strings like `claude-opus-4-5-20251101`, `claude-sonnet-4-5-20250929`, `claude-opus-4-6-20260205`. Claude Code JSONL model names may use these exact strings or slightly different variants. A fuzzy match approach is needed.

**Lookup strategy** (ordered):
1. Exact key match
2. Strip date suffix and match (e.g., `claude-opus-4-5` from `claude-opus-4-5-20251101`)
3. Provider-prefixed match (`anthropic/claude-...`)
4. Not found → `null` → `IsEstimated = true`

```csharp
// Source: pattern derived from ModelContextLimits.cs in project
private ModelPricing? FindPricing(string modelName)
{
    if (_pricingMap.TryGetValue(modelName, out var exact))
        return exact;

    // Try without date suffix
    var stripped = StripDateSuffix(modelName); // reuse from ModelContextLimits
    if (_pricingMap.TryGetValue(stripped, out var stripped_match))
        return stripped_match;

    // Try common variants
    foreach (var prefix in new[] { "anthropic/", "us.anthropic.", "eu.anthropic." })
    {
        if (_pricingMap.TryGetValue(prefix + modelName, out var prefixed))
            return prefixed;
    }

    return null;
}
```

### Pattern 5: Shimmer Animation in WinUI 3

**What:** Replace value `TextBlock` elements with `Border` elements that animate background color between dark gray and lighter gray using a `Storyboard` with `ColorAnimation`. Shimmer is shown by toggling `Visibility` on the `Border` vs the real `TextBlock`.

**When to use:** `IsAggregating` property on ViewModel controls visibility; no layout jump because shimmer occupies the same grid row as the real value.

```csharp
// Source: Uno Platform skeleton loader pattern (WinUI Storyboard is identical API)
// In XAML:
// <Border x:Name="ShimmerBorder" Height="13" CornerRadius="4"
//         Visibility="{x:Bind ViewModel.IsAggregating, Mode=OneWay,
//                     Converter={StaticResource BoolToVisibilityConverter}}" />
// Code-behind triggers storyboard when IsAggregating changes
```

### Anti-Patterns to Avoid

- **Loading entire JSONL on every tab switch:** Filter by timestamp during the initial scan, not by re-reading all files. Use the in-memory `ProjectData` entries with timestamps.
- **HTTP call on UI thread:** Always `await` pricing fetch on background thread; dispatch result to UI thread via `DispatcherQueue.TryEnqueue()`.
- **One HttpClient instance per fetch:** Use the singleton `HttpClient` already in DI.
- **Hardcoding model names for tiered pricing:** Read the `input_cost_per_token_above_200k_tokens` key directly from the LiteLLM JSON — don't hardcode which models are 1M-context.
- **Blocking on LiteLLM fetch at startup:** Fetch asynchronously; show fallback prices immediately while live fetch is in progress.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Segmented/tab bar UI | Custom RadioButton grid with manual selection state | `CommunityToolkit.WinUI.Controls.Segmented` | Keyboard navigation, accessibility, WinUI theming, selection events — all handled |
| Model pricing database | Custom price table in code | LiteLLM `model_prices_and_context_window.json` | 500+ models, updated continuously, tiered pricing already encoded |
| Tiered pricing tier detection | Hardcoded model name list | Read `input_cost_per_token_above_200k_tokens` from LiteLLM JSON | LiteLLM already distinguishes standard vs extended pricing per-model |

**Key insight:** The LiteLLM JSON already encodes all complexity — tiered pricing fields, per-model cache costs, regional variants. Parsing it correctly is far simpler than maintaining a custom price table.

---

## Common Pitfalls

### Pitfall 1: costUSD Availability
**What goes wrong:** Assuming `costUSD` is always present. As of Claude Code v1.0.9+, it may be absent for some plans.
**Why it happens:** Anthropic removed it from Max plan logs in mid-2025 (v1.0.9).
**How to avoid:** Always check `costUSD > 0` before using it; treat null or 0 as "use token fallback".
**Warning signs:** All costs showing as estimated (~) despite having model pricing.

### Pitfall 2: LiteLLM Model Key Mismatch
**What goes wrong:** Claude Code JSONL uses `claude-opus-4-5-20251101`, but LiteLLM JSON keys it as `claude-opus-4-5-20251101` (direct anthropic) or `anthropic.claude-opus-4-5-20251101-v1:0` (bedrock). The direct Anthropic provider key is what matches.
**Why it happens:** LiteLLM has multiple provider entries for the same model.
**How to avoid:** Build the pricing map by extracting model name from the key (strip provider prefix). Prefer direct anthropic entries (`litellm_provider == "anthropic"`).
**Warning signs:** All models falling back to estimated cost even when LiteLLM JSON was fetched.

### Pitfall 3: Time Period Aggregation is Slow on Large JSONL History
**What goes wrong:** Scanning all JSONL files for "this month" means reading potentially hundreds of files.
**Why it happens:** Today/Week/Month require scanning beyond the newest session file.
**How to avoid:** Keep timestamp-indexed in-memory records in `ProjectData`. Filter in memory, not by re-reading files.
**Warning signs:** UI hangs for seconds when switching to Monat tab.

### Pitfall 4: Segmented Control Theming Mismatch
**What goes wrong:** `CommunityToolkit.WinUI.Controls.Segmented` uses its own default colors that don't match the styleguide colors (`#38383A` background, `#636366` active).
**Why it happens:** The toolkit applies WinUI system theme brushes, not custom colors.
**How to avoid:** Override via `ItemContainerStyle` and explicit `Background`/`Foreground` setters in XAML. Do not use `PivotSegmentedStyle` — use the default style with custom theme overrides.
**Warning signs:** Tab bar looks like a generic WinUI control, not a macOS-style pill.

### Pitfall 5: SECURITY — No LiteLLM URL in Credential Manager
**What goes wrong:** Hardcoding the GitHub raw URL in source is fine (it's public), but if using a proxy or API key, it must go through Credential Manager.
**Why it happens:** GitHub raw URL for `model_prices_and_context_window.json` is public — no auth required.
**How to avoid:** Fetch directly from `https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json` with no auth headers. This URL is permitted per CLAUDE.md (raw.githubusercontent.com is allowed).
**Warning signs:** N/A for public URL; flag if this ever changes to a private endpoint.

### Pitfall 6: Burn Rate Division by Zero
**What goes wrong:** Burn rate calculation with no entries in the time window produces divide-by-zero or NaN.
**Why it happens:** Short sessions or switching to Monat tab with little data.
**How to avoid:** Guard with `if (windowMinutes <= 0 || totalTokens == 0) return 0`.
**Warning signs:** Burn rate showing NaN or infinity in UI.

---

## Code Examples

### LiteLLM JSON Field Names (Verified)

```csharp
// Source: https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json
// Verified by direct file inspection 2026-03-16

public record ModelPricing
{
    [JsonPropertyName("input_cost_per_token")]
    public double InputCostPerToken { get; init; }

    [JsonPropertyName("output_cost_per_token")]
    public double OutputCostPerToken { get; init; }

    [JsonPropertyName("cache_creation_input_token_cost")]
    public double? CacheCreationCost { get; init; }

    [JsonPropertyName("cache_read_input_token_cost")]
    public double? CacheReadCost { get; init; }

    // Tiered pricing for 1M-context models (COST-04)
    [JsonPropertyName("input_cost_per_token_above_200k_tokens")]
    public double? InputCostAbove200k { get; init; }

    [JsonPropertyName("output_cost_per_token_above_200k_tokens")]
    public double? OutputCostAbove200k { get; init; }

    [JsonPropertyName("cache_creation_input_token_cost_above_200k_tokens")]
    public double? CacheCreationCostAbove200k { get; init; }

    [JsonPropertyName("cache_read_input_token_cost_above_200k_tokens")]
    public double? CacheReadCostAbove200k { get; init; }

    [JsonPropertyName("litellm_provider")]
    public string? LitellmProvider { get; init; }

    [JsonPropertyName("max_input_tokens")]
    public long? MaxInputTokens { get; init; }
}
```

### JsonlEntry Extension (costUSD field)

```csharp
// Source: confirmed from ccusage documentation + project JsonlEntry.cs pattern
// costUSD is a TOP-LEVEL field on the JsonlEntry, not nested inside message

// Add to JsonlEntry.cs:
[JsonPropertyName("costUSD")]
public decimal? CostUsd { get; init; }
```

### CommunityToolkit Segmented XAML

```xml
<!-- Source: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/segmented/ -->
<!-- xmlns:controls="using:CommunityToolkit.WinUI.Controls" -->
<controls:Segmented
    x:Name="StatisticsTabBar"
    HorizontalAlignment="Stretch"
    SelectedIndex="{x:Bind ViewModel.SelectedTabIndex, Mode=TwoWay}"
    SelectionMode="Single"
    SelectionChanged="OnTabSelectionChanged">
    <controls:SegmentedItem Content="Session" />
    <controls:SegmentedItem Content="Heute" />
    <controls:SegmentedItem Content="Woche" />
    <controls:SegmentedItem Content="Monat" />
</controls:Segmented>
```

### Shimmer Pattern (WinUI 3 Storyboard)

```xml
<!-- Source: Storyboard/ColorAnimation is standard WinUI 3 animation API -->
<!-- Shimmer Border replaces real TextBlock during IsAggregating = true -->
<Border x:Name="InputShimmer"
        Height="13" Width="60" CornerRadius="4"
        HorizontalAlignment="Right"
        Visibility="{x:Bind ViewModel.IsAggregating, Mode=OneWay,
                    Converter={StaticResource BoolToVisibilityConverter}}">
    <Border.Background>
        <SolidColorBrush x:Name="InputShimmerBrush" Color="#38383A"/>
    </Border.Background>
</Border>
```

```csharp
// Code-behind: start shimmer when aggregation begins
private void StartShimmerAnimation()
{
    var animation = new ColorAnimation
    {
        From = Color.FromArgb(0xFF, 0x38, 0x38, 0x3A),
        To = Color.FromArgb(0xFF, 0x55, 0x55, 0x58),
        Duration = new Duration(TimeSpan.FromSeconds(0.8)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever
    };
    Storyboard.SetTarget(animation, InputShimmerBrush);
    Storyboard.SetTargetProperty(animation, "Color");
    var sb = new Storyboard();
    sb.Children.Add(animation);
    sb.Begin();
}
```

### StatisticsSummary Record

```csharp
// Source: extends TokenSummary pattern from project
public record StatisticsSummary
{
    public static readonly StatisticsSummary Empty = new();

    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheCreationTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
    public decimal TotalCostUsd { get; init; }
    public bool HasEstimatedCosts { get; init; }
    public IReadOnlyList<string> Models { get; init; } = [];
}
```

### LiteLLM Fetch and Cache Pattern

```csharp
// Source: follows established ClaudeApiService pattern in project
private const string PricingUrl = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";
private const string CacheFileName = "litellm-pricing-cache.json";
private const int CacheValidHours = 12;

public async Task EnsurePricesLoadedAsync()
{
    if (_pricingMap.Count > 0 && !IsCacheExpired())
        return;

    try
    {
        var json = await _httpClient.GetStringAsync(PricingUrl);
        ParseAndStore(json);
        SaveToLocalCache(json);
        _source = PricingSource.Live;
        _lastFetch = DateTimeOffset.UtcNow;
    }
    catch
    {
        LoadFallback(); // embedded resource or local cache file
        _source = PricingSource.Fallback;
    }
}
```

### Burn Rate Calculation

```csharp
// Source: derived from spec FA-074 requirement
// Uses entries with timestamps within a rolling 60-minute window
public static double ComputeBurnRate(
    IEnumerable<(DateTimeOffset Timestamp, long Tokens)> entries,
    int windowMinutes = 60)
{
    var cutoff = DateTimeOffset.UtcNow.AddMinutes(-windowMinutes);
    var recent = entries.Where(e => e.Timestamp >= cutoff).ToList();

    if (recent.Count == 0 || windowMinutes <= 0)
        return 0;

    var totalTokens = recent.Sum(e => e.Tokens);
    return totalTokens / (double)windowMinutes * 60; // tokens per hour
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| TokenSummary (input + output only) | StatisticsSummary (input + output + cache creation + cache read + cost) | Phase 5 | Richer model; old record is replaced, not extended |
| costUSD always present in JSONL | costUSD may be absent (Claude Code >= v1.0.9) | June 2025 | Must have robust fallback |
| Manual RadioButton grid for segmented | CommunityToolkit.WinUI.Controls.Segmented | 2024 | Official toolkit solution available |

**Deprecated/outdated:**
- `TokenSummary`: Will be replaced by `StatisticsSummary` for Phase 5 statistics section. The existing `GetTokenSummary(string sessionId)` can remain for backward compatibility until Phase 6.

---

## Open Questions

1. **StatisticsSummary vs TokenSummary coexistence**
   - What we know: `MainViewModel` currently uses `InputTokensText` and `OutputTokensText` from `GetTokenSummary`
   - What's unclear: Whether to replace `TokenSummary` entirely or keep it for the session-tab fast path
   - Recommendation: Keep `TokenSummary` for context window display; add `StatisticsSummary` for the STATISTIKEN section. Both can live simultaneously.

2. **Bundled fallback prices freshness**
   - What we know: Prices change when Anthropic launches new models
   - What's unclear: How frequently the bundled prices need updating
   - Recommendation: Include a minimal JSON with the 5-6 known models (claude-opus-4-5, claude-opus-4-6, claude-sonnet-4-5, claude-sonnet-4-6, claude-haiku-4-5). Accept that new models will show estimated costs until live fetch succeeds.

3. **Time period aggregation for "Monat" with large history**
   - What we know: `ProjectData` has all entries in memory, filtered during `ApplyEntryToProjectData`
   - What's unclear: Whether `ProjectData` stores all entries or just aggregated sums
   - Recommendation: `ProjectData` currently stores only aggregated sums (not raw entries). For time-period filtering, store a compact list of `(Timestamp, InputTokens, OutputTokens, CostUsd, ModelName)` tuples per project — roughly 100 bytes per JSONL entry.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 + Moq 4.20.72 |
| Config file | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category=Unit" -x64` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TOKS-02 | SelectedTabIndex property changes trigger IsAggregating = true | unit | `dotnet test ... --filter "FullyQualifiedName~StatisticsSummaryTests"` | ❌ Wave 0 |
| TOKS-03 | GetStatistics(Today) includes subagent tokens | unit | `dotnet test ... --filter "FullyQualifiedName~JsonlServiceTests"` | ✅ (existing file, new test) |
| TOKS-04 | Duplicate uuid+requestId entries not double-counted across time periods | unit | `dotnet test ... --filter "FullyQualifiedName~JsonlServiceTests"` | ✅ (existing file, new test) |
| COST-01 | LiteLLM fetch populates pricing map | unit (mock HttpClient) | `dotnet test ... --filter "FullyQualifiedName~LiteLLMPricingServiceTests"` | ❌ Wave 0 |
| COST-02 | costUSD > 0 used directly; fallback to token * price | unit | `dotnet test ... --filter "FullyQualifiedName~CostCalculatorTests"` | ❌ Wave 0 |
| COST-03 | IsEstimated = true when model not in pricing map | unit | `dotnet test ... --filter "FullyQualifiedName~CostCalculatorTests"` | ❌ Wave 0 |
| COST-04 | Tokens > 200K use _above_200k_tokens price tier | unit | `dotnet test ... --filter "FullyQualifiedName~CostCalculatorTests"` | ❌ Wave 0 |
| COST-05 | BurnRateCalculator returns 0 for empty entries | unit | `dotnet test ... --filter "FullyQualifiedName~BurnRateCalculatorTests"` | ❌ Wave 0 |
| COST-06 | Settings displays PricingSource and LastFetch | manual | visual inspection in SettingsView | manual-only |
| DATA-05 | Fallback prices loaded when HTTP fetch fails | unit (mock HttpClient throwing) | `dotnet test ... --filter "FullyQualifiedName~LiteLLMPricingServiceTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64 --no-build`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Models/StatisticsSummaryTests.cs` — covers TOKS-02 (ViewModel loading state)
- [ ] `CCInfoWindows.Tests/Services/LiteLLMPricingServiceTests.cs` — covers COST-01, DATA-05 (mocked HttpClient)
- [ ] `CCInfoWindows.Tests/Services/CostCalculatorTests.cs` — covers COST-02, COST-03, COST-04
- [ ] `CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs` — covers COST-05
- [ ] New test methods in existing `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — covers TOKS-03, TOKS-04 for time periods

---

## Sources

### Primary (HIGH confidence)
- Direct file read: `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\JsonlService.cs` — existing architecture, ProjectData structure, deduplication pattern
- Direct file read: `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Models\JsonlEntry.cs` — existing JSONL model fields
- Direct file inspection: `https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json` — LiteLLM JSON structure, exact field names, Claude model entries
- Official docs: `https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/segmented/` — Segmented control XAML usage, SelectionMode, SelectedIndex

### Secondary (MEDIUM confidence)
- ccusage documentation `https://ccusage.com/guide/cost-modes` — costUSD top-level field confirmed, three cost modes (auto/calculate/display)
- NuGet: `https://www.nuget.org/packages/CommunityToolkit.WinUI.Controls.Segmented` — version 8.2.251219 current, MIT license

### Tertiary (LOW confidence)
- WebSearch result: costUSD removed from Claude Code >= v1.0.9 Max plan — single source claim, not officially documented; treat as real risk
- LiteLLM docs: `https://docs.litellm.ai/docs/providers/anthropic` — model key format for direct anthropic provider

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — CommunityToolkit Segmented is official; LiteLLM URL verified by direct file fetch
- Architecture: HIGH — follows established project patterns (JsonlService, DI, DispatcherQueue)
- LiteLLM JSON field names: HIGH — verified by direct file inspection
- costUSD availability: MEDIUM — one report of removal in v1.0.9; fallback strategy covers this
- Pitfalls: HIGH — derived from direct code inspection of existing JsonlService

**Research date:** 2026-03-16
**Valid until:** 2026-04-16 (stable stack; LiteLLM pricing schema changes infrequently)
