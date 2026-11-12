# ClaudeUsageWidget

A small Windows 11 taskbar widget that shows how much of your Claude subscription limits
(the 5-hour window and the weekly limit) is left, and when you will hit the limit if you keep
working at the current pace.

```
5h 69% +1h8m
Wk 18% Fri 16:19
```

- **Number**: share of the window that is still left.
- **Tail**: forecast at the current pace; its **color** is the verdict - green: lasts until the
  reset, amber: limit shortly after the reset, red: limit before the reset.
  - 5h: when the limit falls relative to the reset. `+1h8m` = 1h8m of margin after the reset,
    `-30m` = you would sit blocked for 30 minutes before the reset.
  - Wk: local time when you will hit the limit (`until Sun 09:00` once it is already reached).
  - `OK`: no usage to forecast from right now.
- **Click**: details (reset time, pace, forecast); close them with the X, Esc, or by switching to
  any other window. **Drag**: move the strip along the taskbar.
  **Right-click**: refresh, start with Windows, reset position, exit.
- **Popup warning** for the 5-hour window when the forecast says you will be blocked for at least
  an hour: at 2h, 1h, 30m and 15m before the predicted limit (each threshold once per window).

## Requirements

- Windows 11 (taskbar at the bottom; Windows 10 likely works but is not tested).
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- Claude Code signed in with a Pro/Max subscription. The widget reads the OAuth token Claude Code
  stores in `%USERPROFILE%\.claude\.credentials.json` (or `%CLAUDE_CONFIG_DIR%`).

## How it works

- Every 5 minutes it calls `GET https://api.anthropic.com/api/oauth/usage`, the same
  **undocumented** endpoint Claude Code uses for `/usage`. It may change or break without notice.
- The token is only read, never refreshed: Claude Code owns it. If it expires (Claude Code not
  used for a while), the strip turns grey and shows the last known values until Claude Code
  renews the token.
- Each poll is appended to `%LOCALAPPDATA%\ClaudeUsageWidget\history.jsonl` (kept 8 days).
  The pace is the change over the last hour; with less than 20 minutes of history it falls back
  to the average since the window started.
- Windows 11 has no supported API to embed into the taskbar, so the strip is a top-most window
  placed over the empty taskbar area left of the notification area. It hides while a full-screen
  app is in front and while an auto-hidden taskbar is hidden. Primary monitor only.

## Build and run

```powershell
dotnet test
dotnet run --project src/ClaudeUsageWidget
# Single-file exe:
dotnet publish src/ClaudeUsageWidget -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

`ClaudeUsageWidget.exe --demo-alert` shows a sample warning popup on start.

## Repository rules

All content is English-only: no Cyrillic in code, docs, file names or commit messages.
Enable the local check once per clone:

```powershell
git config core.hooksPath .githooks
```

CI runs the same check (`scripts/check-no-cyrillic.ps1`).
