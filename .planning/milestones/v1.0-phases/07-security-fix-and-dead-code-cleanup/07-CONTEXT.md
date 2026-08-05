# Phase 7: Security Fix & Dead Code Cleanup - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Secure WebViewBridge cleanup on logout and removal of all dead code identified in the v1.0 milestone audit. No new features — pure hygiene phase.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — pure infrastructure phase.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `WebViewBridge.cs` — has CoreWebView2 reference and WebMessageReceived handler that need cleanup on logout
- `IWebViewBridge.cs` — interface contract; needs Reset() method added
- `LoginViewModel.cs` — handles logout flow, needs to call WebViewBridge.Reset()

### Established Patterns
- DI-registered services with interface contracts
- CommunityToolkit.Mvvm messenger for cross-component communication
- `[ObservableProperty]` source generators for bindable properties

### Integration Points
- `LoginViewModel` logout flow → needs WebViewBridge.Reset() call
- `CostCalculator.cs` + `CostCalculatorTests.cs` → dead code to remove
- `JsonlDataUpdatedMessage.cs` + `SessionSelectedMessage.cs` → dead messages to remove
- `MainViewModel._inputTokensText` + `_outputTokensText` → dead fields to remove

</code_context>

<specifics>
## Specific Ideas

No specific requirements — infrastructure phase.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>
