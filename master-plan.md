# Master Plan: Desktop Voice Typing App

## Goal
Build a small Windows desktop app that stays running in the background, opens a movable floating voice-typing window, and types dictated text into whichever field currently has focus.

## Recommended stack
- **Language:** C#
- **UI:** WinUI 3 or WPF
- **Background behavior:** tray app + global hotkey + optional startup registration
- **Speech engine:** local NeMo / ONNX runtime using the copied model in `models\nemotron-speech-streaming-en-0.6b-generic-cpu-3\v3`

## What the app should do
1. Run silently in the background after launch.
2. Show a compact floating panel like Windows Voice Typing.
3. Keep the panel draggable and always-on-top when needed.
4. Start/stop recording from a global shortcut.
5. Insert recognized text into the active window at the caret.
6. Optionally auto-start with Windows.

## Suggested architecture
- **Tray host**: keeps the app alive and exposes settings / exit.
- **Floating panel**: microphone button, listening indicator, close/minimize, settings.
- **Hotkey service**: registers a shortcut like `Win+Shift+V` or `Ctrl+Alt+V`.
- **Audio capture pipeline**: grabs mic input and streams it to the model.
- **Inference layer**: runs the ONNX model locally.
- **Text injector**: sends text to the focused control via clipboard/paste or keyboard events.

## Build phases
### Phase 1: Shell
- Create the desktop app window.
- Add tray icon and background startup.
- Add draggable floating UI.
- Add global hotkey.

### Phase 2: Speech
- Load the model from `models\nemotron-speech-streaming-en-0.6b-generic-cpu-3\v3`.
- Wire mic capture to model inference.
- Show partial transcription while listening.

### Phase 3: Typing
- Insert transcribed text into the active app.
- Support commit / cancel / pause.
- Add simple punctuation handling.

### Phase 4: Polish
- Persist settings.
- Add startup toggle.
- Add error handling, mic permissions, and status feedback.

## Shortcut recommendation
Use `Win+Shift+V` if available. If that conflicts, fall back to `Ctrl+Alt+V`.

## Later improvements
- Wake word / push-to-talk modes.
- Better punctuation and command phrases.
- Language switching.
- Multi-monitor position memory.
- Better model packaging and update flow.
