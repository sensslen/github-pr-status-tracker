# GitHub PR Status Tracker

A small cross-platform (Windows + macOS) tray application that keeps an eye on the GitHub
pull requests you care about and tells you when one of them needs you.

It answers one question, quickly, without opening a browser:

> **Which PRs are waiting on me right now?**

## What it does

- **Lists open PRs you've been asked to review** — the ones where you are a requested
  reviewer (directly or through a team).
- **Gathers every PR you authored into one tab**, and calls out the ones wanting something:
  ready to merge, CI failing, a merge conflict, changes requested, nobody reviewing, or
  comments you have not read.
- **Classifies every PR relative to you** — awaiting your review, changes requested,
  approved and ready to merge, CI failing, merge conflict, draft, and so on. See
  [Status model](ARCHITECTURE.md#4-status-model).
- **Notifies you when a PR's state changes** — a review lands on your PR, a new PR is
  assigned to you, CI goes red, your PR becomes mergeable. Notifications use native OS
  toasts.
- **Lets you star the PRs you're actively reviewing** — starred PRs sort to the top of
  their tab. The star is local to the app; nothing is sent to GitHub.
- **Flags comments you haven't read** on your own PRs, cleared by opening the PR. Resolved review
  threads don't count — a conversation you've settled isn't waiting on you. A PR with nothing else
  outstanding says *New comments* on its status pill; otherwise a 💬 sits beside it. This is a
  marker, not a toast. Read state is local to the app, seeded quiet on first run so you don't
  get a backlog.
- **Keeps a notification history** — every notification is written to disk so you can
  dismiss it, come back to it later, and see what you missed while away.

## How it behaves

| Interaction | Result |
|---|---|
| Runs at login | Starts minimized to the system tray / menu bar |
| Hover the tray icon | Tooltip with a brief summary (e.g. `3 need you · 2 waiting · 7 yours (1 ready · 2 need attention)`) |
| Click the tray icon | Opens the main window |
| Right-click the tray icon | Menu: open main window, refresh, open GitHub, quit |
| Main window | Tabs per bucket (*Needs you*, *Waiting on others*, *Your PRs*), plus Notifications and Settings; every row opens its PR |
| Your PRs tab | Everything you authored, whatever state it's in. A strip above the rows counts what's calling for attention (`2 ready to merge · 1 CI failing`), and those rows sort to the top |
| Star a row (☆) | Pins that PR to the top of its tab; persists across restarts. Click ★ again to unstar |
| Filter bar | On every PR tab: free text (title, repo, author, label, `#number`) plus label / repo dropdowns, and author where there's more than one. *Needs you* and *Waiting on others* share a bar; *Your PRs* has its own, so narrowing one never narrows the other. Never affects the tray |
| Toast notification | One toast per poll, however many PRs changed; clicking it opens the PR (or GitHub's PR list for a batch) |
| Notification centre (in-app) | Persisted, dismissible list of past notifications |

## Requirements

- **[GitHub CLI (`gh`)](https://cli.github.com/)** installed and authenticated
  (`gh auth login`). The app shells out to `gh` for all GitHub access — it never asks
  you for a token and never stores credentials of its own.
- **No .NET install** for a released build; it is native. Building from source needs the
  [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Windows 10 1809+ or macOS 12+.

## Getting started

Sign in to the GitHub CLI once (the app reuses its credentials):

```bash
gh auth login
```

### Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Same command on Windows
and macOS:

```bash
dotnet build
```

### Run

```bash
dotnet run --project src/PrStatusTracker
```

Pass `--tray` to start hidden in the tray (this is what the start-at-login entry does):

```bash
dotnet run --project src/PrStatusTracker -- --tray
```

To enable start-at-login, use the toggle in the app's settings; it writes the platform's
native autostart entry (Registry `Run` key on Windows, a `LaunchAgent` plist on macOS).

### Test

```bash
dotnet test
```

Every pull request builds and runs them on Windows and macOS
(`.github/workflows/ci.yml`).

### Publish

A Release publish with a runtime identifier produces a **NativeAOT build**: a native executable
that needs no .NET install, next to the three Avalonia native libraries it loads at run time
(Skia, HarfBuzz, ANGLE). The settings live in the csproj, so the command is just:

**Windows (x64)**

```bash
dotnet publish src/PrStatusTracker -c Release -r win-x64
```

**macOS (Apple Silicon / Intel)**

```bash
dotnet publish src/PrStatusTracker -c Release -r osx-arm64
```

```bash
dotnet publish src/PrStatusTracker -c Release -r osx-x64
```

Output: `src/PrStatusTracker/bin/Release/net10.0/<rid>/publish/`, about 37 MB.

NativeAOT links with the platform C++ toolchain: the Visual Studio *Desktop development with C++*
workload on Windows, the Xcode command-line tools on macOS. Without it the link step fails
(`vswhere.exe` not found on Windows), and `-p:PublishAot=false` falls back to a single-file build
that carries the .NET runtime with it instead.

### Installers

Pushing a `vX.Y.Z` tag publishes a GitHub release with a Windows installer and a disk image per
Mac architecture (`.github/workflows/release.yml`). Nothing is signed, so Windows shows a
SmartScreen warning and macOS asks before opening the app.

To build either one by hand from a publish directory:

**Windows** — [NSIS](https://nsis.sourceforge.io/) 3:

```bash
makensis /DVERSION=1.2.3 /DFILEVERSION=1.2.3.0 \
         /DSOURCE=<absolute publish dir> /DOUTFILE=<absolute setup.exe path> \
         packaging/windows/installer.nsi
```

Both paths have to be absolute. makensis compiles from the script's own directory, so a
relative source fails to open and a relative output lands in `packaging/windows/`.

It installs for the current user, so it needs no administrator, and it registers the Start Menu
shortcut and the notification identity that give toasts the app's name and icon rather than
`PrStatusTracker`.

**macOS**:

```bash
packaging/macos/make-bundle.sh <publish dir> 1.2.3 "PR Status Tracker.app"
```

It wraps the publish output in a menu-bar (`LSUIElement`) bundle and ad-hoc signs it. A Developer
ID signature and notarization, which is what native `UNUserNotificationCenter` notifications
would need, are not set up.

Both read their icons from `packaging/icon/`, which `packaging/icon/make-icons.py` regenerates
from the app's own window icon.

## Tech stack

- **C# / .NET 10**
- **[Avalonia](https://avaloniaui.net/)** for the cross-platform UI
- **[Avalonia.Markup.Declarative](https://github.com/AvaloniaCommunity/Avalonia.Markup.Declarative)**
  — UI is written in C#, not XAML
- **`gh` CLI** as the GitHub transport (a single GraphQL query via `gh api graphql`)

## Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) — architecture, status model, data flow, and the
  implementation plan.

## Status

Working prototype. Phases 1–7 of the [implementation plan](ARCHITECTURE.md#implementation-plan)
are in place: tray icon with tooltip summary, `gh`-backed polling, the status model with
tests, change detection, native toasts (Windows verified; macOS untested), persisted
notification history, starred PRs, the Your PRs tab with its callouts and unread-comment
tracking, settings, start-at-login, and the installers. Signing and notarization are not
set up.
