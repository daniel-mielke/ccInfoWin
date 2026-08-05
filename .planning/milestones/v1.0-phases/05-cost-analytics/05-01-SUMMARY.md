---
phase: 05-cost-analytics
plan: 01
subsystem: cost-analytics-backend
tags: [cost, pricing, jsonl, burn-rate, tdd]
dependency_graph:
  requires: [04-local-data-pipeline]
  provides: [StatisticsSummary, ModelPricing, IPricingService, LiteLLMPricingService, CostCalculator, BurnRateCalculator, IJsonlService.GetStatistics]
  affects: [JsonlService, AppSettings, JsonlEntry]
tech_stack:
  added: []
  patterns: [TDD red-green, IPricingService singleton, embedded resource fallback, in-memory EntryLog for time-period filtering]
key_files:
  created:
    - CCInfoWindows/CCInfoWindows/Models/TimePeriod.cs
    - CCInfoWindows/CCInfoWindows/Models/StatisticsSummary.cs
    - CCInfoWindows/CCInfoWindows/Models/ModelPricing.cs
    - CCInfoWindows/CCInfoWindows/Helpers/CostCalculator.cs
    - CCInfoWindows/CCInfoWindows/Helpers/CostFormatter.cs
    - CCInfoWindows/CCInfoWindows/Helpers/BurnRateCalculator.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IPricingService.cs
    - CCInfoWindows/CCInfoWindows/Services/LiteLLMPricingService.cs
    - CCInfoWindows/CCInfoWindows/Resources/fallback-prices.json
    - CCInfoWindows.Tests/Helpers/CostCalculatorTests.cs
    - CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs
    - CCInfoWindows.Tests/Services/LiteLLMPricingServiceTests.cs
  modified:
    - CCInfoWindows/CCInfoWindows/Models/JsonlEntry.cs
    - CCInfoWindows/CCInfoWindows/Models/AppSettings.cs
    - CCInfoWindows/CCInfoWindows/Services/Interfaces/IJsonlService.cs
    - CCInfoWindows/CCInfoWindows/Services/JsonlService.cs
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
    - CCInfoWindows.Tests/Services/JsonlServiceTests.cs
decisions:
  - "EntryLogItem class stores per-entry token breakdown (not just total) in ProjectData, enabling time-period aggregation with full split (input/output/cache creation/cache read)"
  - "NullPricingService inner class used when IPricingService not injected (backward compatibility for existing tests and DI setup)"
  - "FindPricing does bidirectional date-suffix matching: strips query AND scans map keys with stripped suffix to handle both query patterns"
  - "BurnRateEntries in StatisticsSummary stores (Timestamp, TotalTokens) tuples from filtered entries; consumed by BurnRateCalculator in ViewModel layer"
metrics:
  duration: 53min
  completed_date: "2026-03-16T12:13:15Z"
  tasks: 2
  files: 16
---

# Phase 05 Plan 01: Cost Analytics Backend Summary

Cost analytics backend with LiteLLM pricing integration, per-entry cost calculation using costUSD primary and token*price fallback, tiered 200K pricing, time-period aggregation with deduplication, and burn rate calculation.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Models, interfaces, helpers, and cost calculation with tests | 2baa9c6 | TimePeriod.cs, StatisticsSummary.cs, ModelPricing.cs, JsonlEntry.cs+, AppSettings.cs+, CostCalculator.cs, CostFormatter.cs, BurnRateCalculator.cs, IPricingService.cs, 2 test files |
| 2 | LiteLLM pricing service, JsonlService time-period aggregation, and tests | ce1125c | LiteLLMPricingService.cs, fallback-prices.json, IJsonlService.cs+, JsonlService.cs+, CCInfoWindows.csproj+, 2 test files |

## Test Results

- **CostCalculatorTests**: 8 tests green (costUSD primary, token fallback, tiered 200K, cache tokens, null usage)
- **BurnRateCalculatorTests**: 4 tests green (empty, rolling window, outside window, zero minutes)
- **LiteLLMPricingServiceTests**: 5 tests green (live fetch, fallback, exact match, date-stripped match, unknown model)
- **JsonlServiceTests.GetStatistics**: 4 tests green (session aggregation, today filter, cross-project dedup, BurnRateEntries)
- **Total new tests**: 21

## Key Decisions Made

1. **EntryLogItem with full token breakdown**: Storing per-entry input/output/cache breakdown (not just total) in ProjectData.EntryLog allows time-period aggregation to return correctly split StatisticsSummary without re-reading JSONL files.

2. **NullPricingService for backward compatibility**: Adding IPricingService as an optional constructor parameter with a NullPricingService default preserves all existing JsonlService tests and DI registrations without modification.

3. **Bidirectional date-suffix matching in FindPricing**: LiteLLM map keys have date suffixes (e.g. `claude-sonnet-4-5-20250929`) but callers may query with either the full key or the stripped name. FindPricing scans map keys by stripping their suffixes to handle both directions.

4. **DeduplicationKey in EntryLogItem**: Storing the `uuid|requestId` deduplication key in EntryLogItem (alongside token data) enables TOKS-04 cross-project deduplication during time-period aggregation without a separate data structure.

## Deviations from Plan

None — plan executed exactly as written.

## Requirements Satisfied

| Requirement | Description | Verified By |
|-------------|-------------|-------------|
| TOKS-03 | Subagent tokens included in all time period aggregations | BuildTimePeriodStatistics iterates all _projectData.Values including subagent projects |
| TOKS-04 | Deduplication by uuid+requestId across projects | DeduplicationKey stored in EntryLogItem, seenIds HashSet in BuildTimePeriodStatistics |
| COST-01 | LiteLLM prices fetched live from GitHub raw URL with 12h cache | LiteLLMPricingService with CacheValidHours=12 |
| COST-02 | Costs from costUSD field; fallback to token * price | CostCalculator.ComputeCost primary/fallback logic |
| COST-03 | Estimated costs flagged when model not in pricing DB | CostCalculator returns isEstimated=true when pricing is null |
| COST-04 | Tiered pricing for 1M-context models above 200K | CostCalculator TierBreakpointTokens=200_000 with InputCostAbove200k |
| COST-05 | Burn rate calculated | BurnRateCalculator.ComputeBurnRate with 60-min rolling window |
| COST-06 | AppSettings extended for pricing source/fetch time | AppSettings.PricingSource and LastPricingFetch added |
| DATA-05 | Fallback to bundled prices | LiteLLMPricingService loads EmbeddedResource fallback-prices.json |

## Self-Check: PASSED
