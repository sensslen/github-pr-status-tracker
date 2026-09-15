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
| Hover the tray icon | Tooltip with a brief summary (e.g. `3 need you · 2 waiting on others`) |
| Click the tray icon | Opens the full PR list window |
| Toast notification | Fired on any PR state change; clicking it opens that PR on GitHub |
| Notification centre (in-app) | Persisted, dismissible list of past notifications |

## Requirements

- **[GitHub CLI (`gh`)](https://cli.github.com/)** installed and authenticated
  (`gh auth login`). The app shells out to `gh` for all GitHub access — it never asks
  you for a token and never stores credentials of its own.
- **.NET 10 runtime** (or a self-contained build, which bundles it).
- Windows 10 1809+ or macOS 12+.

## Getting started

```bash
gh auth login
```

```bash
dotnet run --project src/PrStatusTracker
```

To enable start-at-login, use the toggle in the app's settings; it writes the platform's
native autostart entry (Registry `Run` key on Windows, a `LaunchAgent` plist on macOS).

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

Early — design documented, implementation not yet started. See the
[Implementation plan](ARCHITECTURE.md#implementation-plan).
