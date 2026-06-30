# NeMo Voice Typing

A tiny Windows desktop voice-typing app. Stays in the tray, opens a floating
panel, transcribes speech locally with the NVIDIA NeMo streaming ASR model,
and types the result into whatever has focus.

## Requirements
- Windows 10/11 (x64)
- .NET 8 SDK
- NVIDIA NeMo streaming model files under `models/nemotron-speech-streaming-en-0.6b-generic-cpu-3/v3/`
  (gitignored — copy them in manually)

## Run
```powershell
dotnet run --project src/VoiceTyping
```

## Hotkey
`Ctrl+Shift+A` — toggle dictation (one-handed, left-hand cluster). Editable in
`%APPDATA%\VoiceTyping\config.json`.
