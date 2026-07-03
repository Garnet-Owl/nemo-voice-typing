# Changelog

All notable changes to Nemo Voice Typing are documented here. Format follows
[Keep a Changelog v1.1.0](https://keepachangelog.com/en/1.1.0/), adapted for
ticket-driven work — newest entry always at the top.

## [Unreleased]

### Added
- Unit test project (`tests/unit`, xUnit, given-when-then style) wired into the solution.
- Release notes are now derived from `CHANGELOG.md`: the workflow embeds the newest changelog section in each GitHub release and appends GitHub's auto-generated commit list (`--generate-notes`).

### Fixed
- `StartupRegistration` no longer falls back to `Assembly.Location` (empty in single-file publishes, warning IL3000); the run-at-startup registry command is built from `Environment.ProcessPath` with an `AppContext.BaseDirectory` fallback, covered by regression tests.
- Floating pill can no longer be dragged or restored off-screen: it now clamps to the work area (screen minus taskbar) of its current monitor on drag-drop, on startup, and whenever shown (`FloatingPanel.xaml.cs`), with DPI-aware Win32 monitor lookup.

### Changed
- Rewrote `CLAUDE.md` for this C#/.NET 8 WPF project (was written for a Python project): replaced Python/Gemini-specific rules with C# conventions, project layout, and build/publish commands.
- Added this `CHANGELOG.md`.
