---
phase: 01-foundation-and-authentication
plan: 03
subsystem: auth
tags: [token-validation, logout, session-expiry, httpclient, infobar, winui3, mvvm, webview2-cookies]

# Dependency graph
requires:
  - phase: 01-foundation-and-authentication/01-02
    provides: "CredentialService, LoginView, WebView2 cookie extraction, AuthStateChangedMessage"
provides:
  - "Startup token validation against claude.ai/api/organizations"
  - "MainView placeholder with logout button and session-expired InfoBar"
  - "MainViewModel with ValidateTokenAsync, LogoutCommand, ReLoginCommand"
  - "Complete authentication lifecycle (login -> auto-login -> logout -> re-login)"
  - "Cookie clearing via WebView2 CookieManager API on logout"
affects: [02-core-monitoring-dashboard, 04-local-data-pipeline]

# Tech tracking
tech-stack:
  added: [System.Net.Http (HttpClient singleton for API validation)]
  patterns: [startup-routing-via-token-check, cookie-manager-api-cleanup, source-changed-spa-detection]

key-files:
  created:
    - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
    - CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs
  modified:
    - CCInfoWindows/CCInfoWindows/App.xaml.cs
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
    - CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs
    - CCInfoWindows/CCInfoWindows/Views/LoginView.xaml.cs

key-decisions:
  - "Use SourceChanged event for SPA login detection instead of NavigationCompleted (claude.ai is a SPA)"
  - "Clear WebView2 cookies via CookieManager API instead of deleting UDF directory (safer, no file locking)"
  - "Copy WebView2Loader.dll to output root for unpackaged app deployment"
  - "Offline startup with stored token assumes valid (don't block user)"

patterns-established:
  - "Token validation: GET claude.ai/api/organizations with sessionKey cookie, 2xx = valid"
  - "SPA detection: SourceChanged event captures URL changes within single-page apps"
  - "Cookie cleanup: CookieManager.DeleteCookiesAsync for targeted domain cleanup"

requirements-completed: [AUTH-03, AUTH-04, SECU-03]

# Metrics
duration: 152min
completed: 2026-03-09
---

# Phase 1 Plan 3: Auth Lifecycle Summary

**Startup token validation, MainView with logout/expiry InfoBar, and complete auth lifecycle verified end-to-end**

## Performance

- **Duration:** 152 min (includes human verification and 3 bug fixes)
- **Started:** 2026-03-09T17:24:17Z (first commit)
- **Completed:** 2026-03-09T19:56:18Z (last fix commit)
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments
- Startup routing validates stored token against claude.ai API, navigates to MainView (valid) or LoginView (expired/missing)
- MainView placeholder with session-expired InfoBar (non-invasive banner with Re-Login button per locked decision)
- Logout clears both Credential Manager tokens and WebView2 cookies via CookieManager API
- Full auth lifecycle verified by user: first login, auto-login on restart, logout, re-login, window persistence

## Task Commits

Each task was committed atomically:

1. **Task 1: Startup token validation, MainView with logout and expiry handling** - `9cbce00` (feat)
2. **Task 2: Verify complete authentication lifecycle end-to-end** - checkpoint approved by user

**Bug fix commits during verification:**
- `06764d1` (fix) - Copy WebView2Loader.dll to output root for unpackaged app
- `a791d36` (fix) - Use SourceChanged for SPA login detection instead of NavigationCompleted
- `bff7e55` (fix) - Clear cookies via CookieManager API instead of deleting UDF

## Files Created/Modified
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` - Token validation, logout command, expiry handling via ObservableObject + IRecipient
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` - Post-login placeholder with InfoBar and logout button
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml.cs` - Minimal code-behind, DI ViewModel resolution
- `CCInfoWindows/CCInfoWindows/App.xaml.cs` - Startup token check routing, HttpClient DI registration
- `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` - WebView2Loader.dll copy target for unpackaged deployment
- `CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs` - SPA-aware login detection via SourceChanged
- `CCInfoWindows/CCInfoWindows/Views/LoginView.xaml.cs` - Updated event wiring for SourceChanged

## Decisions Made
- **SourceChanged over NavigationCompleted:** Claude.ai is a SPA; NavigationCompleted only fires on initial load. SourceChanged captures in-app URL transitions after login.
- **CookieManager API over UDF deletion:** Deleting the WebView2 User Data Folder while WebView2 may hold file locks is unreliable. CookieManager.DeleteCookiesAsync is the supported API for targeted cookie removal.
- **WebView2Loader.dll copy:** Unpackaged WinUI 3 apps need WebView2Loader.dll in the output root alongside the executable, not just in the runtimes subfolder.
- **Offline = assume valid:** Network errors during token validation return true so users aren't blocked from the app when offline.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] WebView2Loader.dll missing from output root**
- **Found during:** Task 2 (verification)
- **Issue:** App crashed at runtime because WebView2Loader.dll was only in runtimes/ subfolder, not output root
- **Fix:** Added MSBuild target to copy dll to output directory in .csproj
- **Files modified:** CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
- **Verification:** App launches successfully
- **Committed in:** 06764d1

**2. [Rule 1 - Bug] NavigationCompleted not firing for SPA login**
- **Found during:** Task 2 (verification)
- **Issue:** Claude.ai login redirect happens within the SPA; NavigationCompleted doesn't fire for in-app route changes
- **Fix:** Switched to SourceChanged event which fires when the WebView2 source URL changes
- **Files modified:** LoginViewModel.cs, LoginView.xaml.cs
- **Verification:** Login flow detects successful authentication and navigates to MainView
- **Committed in:** a791d36

**3. [Rule 1 - Bug] UDF deletion fails due to file locks**
- **Found during:** Task 2 (verification)
- **Issue:** Deleting WebView2 User Data Folder on logout fails because WebView2 runtime holds file locks
- **Fix:** Used CookieManager.DeleteCookiesAsync API to clear cookies without touching the filesystem
- **Files modified:** MainViewModel.cs, LoginViewModel.cs
- **Verification:** Logout clears session, re-login shows fresh login page
- **Committed in:** bff7e55

---

**Total deviations:** 3 auto-fixed (2 bugs, 1 blocking)
**Impact on plan:** All fixes were necessary for the app to function correctly. No scope creep.

## Issues Encountered
- WinUI 3 unpackaged deployment has WebView2 loader quirks not documented in official guides
- Claude.ai SPA behavior differs from traditional multi-page auth flows, requiring SourceChanged instead of NavigationCompleted

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Complete auth lifecycle operational: login, auto-login, logout, re-login, session expiry banner
- Phase 1 fully complete -- all 3 plans executed and verified
- Phase 2 (Core Monitoring Dashboard) can begin: API polling will use the established HttpClient singleton and session token from CredentialService
- Phase 4 (Local Data Pipeline) can also begin: depends only on Phase 1 foundation

## Self-Check: PASSED

All key files verified present. All commit hashes (9cbce00, 06764d1, a791d36, bff7e55) confirmed in git log.

---
*Phase: 01-foundation-and-authentication*
*Completed: 2026-03-09*
