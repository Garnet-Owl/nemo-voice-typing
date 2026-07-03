# Changelog

All notable changes to Nemo Voice Typing are documented here. Format follows
[Keep a Changelog v1.1.0](https://keepachangelog.com/en/1.1.0/), adapted for
ticket-driven work — newest entry always at the top.

## [Unreleased]

### Fixed
- Floating pill can no longer be dragged or restored off-screen: it now clamps to the work area (screen minus taskbar) of its current monitor on drag-drop, on startup, and whenever shown (`FloatingPanel.xaml.cs`), with DPI-aware Win32 monitor lookup.

### Changed
- Rewrote `CLAUDE.md` for this C#/.NET 8 WPF project (was written for a Python project): replaced Python/Gemini-specific rules with C# conventions, project layout, and build/publish commands.
- Added this `CHANGELOG.md`.
