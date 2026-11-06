# Changelog

Newest entry always at the top. Each entry is written for end users — the
topmost section is copied verbatim into the GitHub release notes by the
release workflow, so keep it behavior-focused, plain-English, and at most
four bullet points.

## 2026-11-06 — A clearer app icon and getting-started guide

- The app and tray now use a cleaner microphone icon that matches the floating control.
- The README now gives a quicker overview of installation, controls, voice commands, requirements, and privacy. A short demo recording script is also available.

## 2026-11-02 — Dictation that works in Notepad

- Dictating into Windows 11 Notepad no longer garbles or drops words, and "scratch that" and "delete last" remove expected content. Notepad loses some of the keystrokes the app sends, so text is now pasted into Notepad instead. Everywhere else, typing works exactly as before.
- Whatever you had copied is put back when dictation stops, including images, files and formatted text. Dictated words are kept out of clipboard history.
- If text comes out garbled in another app, you can set `TextInjection` to `clipboard` in `config.json` to always paste, or set to `keystrokes` to never use the clipboard.

## 2026-09-27 — Native Windows ARM64 builds, steadier dictation

- The app now builds natively for Windows ARM64. Previously it had to run emulated, which is slower. Downloadable releases are still x64 only; ARM64 requires building from source.
- Fixed words occasionally being typed twice, or going missing after a pause.
- Dictation churns through far less memory while running, so longer sessions should feel smoother.

## 2026-09-22 — Better mic pickup, and a memory leak fixed

- The app now boosts your mic input before transcribing, so quieter voices and mics come through more clearly; the level meter on the pill was also recalibrated, since it barely moved at normal speaking volume before.
- Fixed a bug where toggling dictation on and off repeatedly over a session would gradually use more memory and slow down; you no longer need to restart the app to get performance back.

## 2026-07-03 — A mic pill that stays on screen, and control over auto-stop

- The floating mic pill can no longer get lost off the screen: it snaps back into view if you drop it past any edge or under the taskbar, and it rescues itself on startup after a resolution or monitor change.
- Click the empty space on the pill (next to the mic button) to choose how long the mic stays on before it auto-stops — presets from 15 seconds to 1 hour, or a custom amount with a sec/min/hr selector, up to 5 hours. An out-of-range entry shows a short warning like "Can't exceed 5 hours" instead of failing silently. The default is still 30 seconds.
- "Start with Windows" now always registers the correct program path.
- Release notes are now generated from this changelog, so update notifications describe what actually changed.
