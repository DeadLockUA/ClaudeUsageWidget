# Project rules

- Everything committed is English-only: code, comments, docs, UI strings, file names, commit
  messages. No Cyrillic anywhere. `scripts/check-no-cyrillic.ps1` enforces it (pre-commit hook in
  `.githooks/`, and CI).
- Core logic (parsing, forecast, alerts, formatting, history) lives in `src/GitHubUsageWidget.Core`
  and is covered by `tests/`. Keep WPF/Win32 glue in `src/GitHubUsageWidget` thin.
- Design: `docs/superpowers/specs/2026-11-12-claude-usage-widget-design.md`.
