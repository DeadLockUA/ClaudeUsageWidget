# Claude usage taskbar widget - design

Date: 2026-11-12. Status: prototype implemented.

## Intent

See at a glance how much of the Claude subscription limits is left, how fast it is being burned,
and whether it will last until the reset at the current pace. If not, show when the limit will be
hit.

## Goals

- Two windows only: the 5-hour session window and the weekly (all models) window.
- Remaining percent, reset time, pace, and a forecast verdict for each.
- Always visible on the taskbar as a two-line text strip (chosen over a tray icon so the
  limit time is visible without hovering).
- Popup warnings for the 5-hour window only.

## Non-goals

- API (pay-as-you-go) spend in dollars.
- Per-model weekly windows (Fable, Opus, Sonnet).
- Multiple accounts.
- Refreshing the OAuth token (Claude Code owns it; refreshing it here could log Claude Code out).
- Adjusting the forecast for working hours: the user works around the clock, so wall-clock
  time is used.

## Data source

`GET https://api.anthropic.com/api/oauth/usage` with `Authorization: Bearer <token>` and
`anthropic-beta: oauth-2025-04-20`. Undocumented; used by Claude Code's `/usage`. The response
has `five_hour` and `seven_day` objects with `utilization` (0..100, integer in practice) and
`resets_at` (ISO 8601, nullable). Usage is account-wide (Claude Code and claude.ai chats).

Token: `claudeAiOauth.accessToken` in `%USERPROFILE%\.claude\.credentials.json`
(or `%CLAUDE_CONFIG_DIR%\.credentials.json`). Read-only. It is short-lived and renewed by Claude
Code; when it is expired or rejected the widget keeps the last values, greyed out, with a reason.

Polling every 5 minutes. On HTTP 429 back off by `Retry-After`, at least 10 minutes.

## Forecast

- Window start = `resets_at` minus window length (5h or 7d).
- Average pace = used percent / time since window start (needs at least 5 minutes).
- Current pace = change over the last hour from local history (needs at least 20 minutes of
  history in the same window; percents are integers, so shorter spans are too noisy). Preferred
  over the average when available.
- Time to limit = remaining percent / pace. Verdicts:
  - `RunsOut`: limit before reset (red, shows the local limit time);
  - `Tight`: limit within 1.25x the time to reset (amber);
  - `Ok`: otherwise, or no burn (green);
  - `Exhausted`: 100% used (red, shows reset time);
  - `Idle`: window not started (`resets_at` null).
- Two reset times within one minute are the same window (the server jitters sub-second).

## Alerts (5-hour window only)

Thresholds T = 2h, 1h, 30m, 15m. A threshold fires when time to limit <= T and time to reset
>= T + 1h, i.e. the user would sit blocked for at least an hour. Each threshold fires once per
window; if several match at once only the smallest fires and the larger ones are marked as
fired. State is persisted so a restart does not repeat alerts. The weekly window never pops up.

Builder's call: the popup is a custom top-most window above the notification area (not a
Windows toast), auto-closes after 2 minutes, click opens details. No popup on reaching 100%.

## UI

- Strip: `5h 69% +1h8m` / `Wk 18% Fri 16:19`. No status words: the verdict is the tail color.
  5h tail = predicted limit time relative to the reset (`+` margin, `-` blocked time; when already
  exhausted, `-` time until reset). Weekly tail = predicted limit clock time, shown even when green
  (`until <reset>` once exhausted). `OK` when there is no burn to forecast from. Label and percent
  in the taskbar text color; everything grey when stale. Follows the light/dark taskbar
  theme. All UI strings are English (repository rule).
- Placement: top-most tool window over the primary taskbar, right edge next to
  `TrayNotifyWnd`; user-adjustable horizontal offset by dragging (saved). Re-asserts top-most
  every second and on every foreground change. Hidden while a full-screen app is in front and
  while an auto-hidden taskbar is hidden.
- Click: details popup (remaining, reset time and countdown, progress bar, pace with its source,
  verdict, last update / problem). Verdict wording differs per window:
  - 5-hour: `enough +1h18m` / `tight +20m` / `runs out -30m` - the predicted limit time relative
    to the reset (`+` surplus, `-` time you would sit blocked), shown even when green; plain
    `enough` when there is no burn.
  - Weekly: `enough until reset` / `tight: limit ~Fri 17:33` / `runs out Fri 17:33` (clock time). Right-click: Details, Refresh now, Start with Windows,
  Reset position, Exit.

## Storage

`%LOCALAPPDATA%\ClaudeUsageWidget\`: `history.jsonl` (8 days), `settings.json`,
`alert-state.json`, `log.txt` (errors only, capped at 1 MB).

## Stack

C# / .NET 8, WPF. `ClaudeUsageWidget.Core` (pure logic, unit-tested with xUnit) and
`ClaudeUsageWidget` (WPF + Win32 interop). Distributed as a framework-dependent single-file exe.

## Known risks

- The endpoint is undocumented and may change or be rate-limited.
- Overlaying the taskbar is not a supported API; Explorer updates may move `TrayNotifyWnd`.
- If Claude Code is not used for hours the token expires and the data goes stale.
