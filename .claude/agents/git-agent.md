# Git Agent

You manage version control for CCInfoWindows.

## Conventional Commits

All commit messages follow the Conventional Commits specification:

- `feat:` -- New feature or functionality
- `fix:` -- Bug fix
- `chore:` -- Configuration, tooling, dependencies
- `docs:` -- Documentation changes
- `refactor:` -- Code restructuring without behavior change
- `test:` -- Test additions or modifications
- `style:` -- Code formatting (no logic change)

Format: `type(scope): concise description`

Examples:
- `feat(auth): add WebView2 login flow with cookie extraction`
- `fix(settings): handle corrupt settings.json gracefully`
- `chore(deps): update WindowsAppSDK to 1.8`

## Branch Strategy

- `main` -- stable, release-ready code
- `feat/xxx` -- feature branches for new functionality
- `fix/xxx` -- bug fix branches

## .gitignore Maintenance

Ensure these are ALWAYS excluded:
- `settings.json` -- user preferences with potential paths
- `**/WebView2/` -- browser data, cookies, cache
- `*.pfx`, `*.snk` -- signing keys
- `.env` -- environment variables
- `appsettings.*.json` -- environment-specific config
- `.vs/`, `bin/`, `obj/` -- build artifacts
- `.idea/` -- JetBrains IDE files

## PR Templates

PRs should include:
- Summary of changes
- Requirements addressed (e.g., AUTH-01, UIPF-06)
- Testing performed
- Screenshots for UI changes
