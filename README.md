# GitHub PR Status Tracker

A small cross-platform (Windows + macOS) tray application that keeps an eye on the GitHub
pull requests you care about and tells you when one of them needs you.

It answers one question, quickly, without opening a browser:

> **Which PRs are waiting on me right now?**

## What it does

- **Lists open PRs you've been asked to review** — the ones where you are a requested
  reviewer (directly or through a team).
- **Lists open PRs you authored** and where each one stands.
- **Classifies every PR relative to you** — awaiting your review, changes requested,
  approved and ready to merge, CI failing, merge conflict, draft, and so on. See
  [Status model](ARCHITECTURE.md#status-model).
- **Notifies you when a PR's state changes** — a review lands on your PR, a new PR is
  assigned to you, CI goes red, your PR becomes mergeable. Notifications use native OS
  toasts.
- **Keeps a notification history** — every notification is written to disk so you can
  dismiss it, come back to it later, and see what you missed while away.

## How it behaves

| Interaction | Result |
|---|---|
| Runs at login | Starts minimized to the system tray / menu bar |
| Hover the tray icon | Tooltip with a brief summary (e.g. `3 need you · 2 waiting · 1 ready to merge`) |
| Click the tray icon | Compact popup pinned by the tray: a *Needs you / Waiting / Ready* switch with counts, the selected list, and a button to the main window |
| Right-click the tray icon | Menu: open main window, refresh, open GitHub, quit |
| Main window | Tabs per bucket (*Needs you*, *Waiting on others*, *Ready to merge*), plus Notifications and Settings; every row opens its PR |
| Filter bar | On the *Needs you* and *Waiting on others* tabs: free text (title, repo, author, label, `#number`) plus author / label / repo dropdowns; never affects the tray |
| Toast notification | One toast per poll, however many PRs changed; clicking it opens the PR (or GitHub's PR list for a batch) |
| Notification centre (in-app) | Persisted, dismissible list of past notifications |

## Requirements

- **[GitHub CLI (`gh`)](https://cli.github.com/)** installed and authenticated
  (`gh auth login`). The app shells out to `gh` for all GitHub access — it never asks
  you for a token and never stores credentials of its own.
- **.NET 10 runtime** (or a self-contained build, which bundles it).
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

### Publish

Self-contained, single-file builds that run without a .NET install. Output lands in
`src/PrStatusTracker/bin/Release/net10.0/<rid>/publish/`.

**Windows (x64)**

```bash
dotnet publish src/PrStatusTracker -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Run `PrStatusTracker.exe` from the publish folder, or copy it anywhere. Toasts work
out of the box (they go through Windows PowerShell's notification registration).

**macOS (Apple Silicon / Intel)**

```bash
dotnet publish src/PrStatusTracker -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

```bash
dotnet publish src/PrStatusTracker -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true
```

The result is a bare executable, which is enough to run from a terminal. For a proper
menu-bar app (Dock-less, Finder-launchable, real notifications) it needs to be wrapped in a
`.app` bundle with `LSUIElement` set, signed, and notarized — that is the packaging phase in
[ARCHITECTURE.md](ARCHITECTURE.md#8-packaging) and is not scripted yet.

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

Working prototype. Phases 1–6 of the [implementation plan](ARCHITECTURE.md#implementation-plan)
are in place: tray icon with tooltip summary, `gh`-backed polling, the status model with
tests, change detection, native toasts (Windows verified; macOS untested), persisted
notification history, settings, and start-at-login. Packaging (phase 7) has not started.
