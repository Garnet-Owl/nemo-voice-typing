# Changelog

Newest entry always at the top. Each entry is written for end users — the
topmost section is copied verbatim into the GitHub release notes by the
release workflow, so keep it behavior-focused, plain-English, and at most
four bullet points.

## 2026-07-03 — A mic pill that stays on screen, and control over auto-stop

- The floating mic pill can no longer get lost off the screen: it snaps back into view if you drop it past any edge or under the taskbar, and it rescues itself on startup after a resolution or monitor change.
- Click the empty space on the pill (next to the mic button) to choose how long the mic stays on before it auto-stops — presets from 15 seconds to 1 hour, or a custom amount with a sec/min/hr selector, up to 5 hours. An out-of-range entry shows a short warning like "Can't exceed 5 hours" instead of failing silently. The default is still 30 seconds.
- "Start with Windows" now always registers the correct program path.
- Release notes are now generated from this changelog, so update notifications describe what actually changed.
