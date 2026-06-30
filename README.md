# Voice Typing

Tiny Windows tray app that lets you dictate into any focused text field.
Press **Ctrl+Alt+A**, talk, the words appear where your cursor is.
100% offline after the first run — speech recognition runs locally on CPU
via a quantised NVIDIA NeMo streaming model.

## Why

I'm a machine-learning engineer. Most of my "typing" is prompting LLMs and
agents, which is a lot of long-form text and was starting to hurt my hands.
Windows' built-in voice typing is fine, but I wanted something that lives in
the corner of my screen, starts on a single chord I can hit with my left hand,
and ships with a real model — no cloud round-trips, no telemetry. One evening
of hacking, here it is.

## Install

1. Grab `VoiceTyping.exe` from
   [Releases](https://github.com/Garnet-Owl/nemo-voice-typing/releases)
   (or build from source — see below).
2. Double-click to run. A small pill appears on the right edge of your screen
   and a microphone icon shows up in the system tray.
3. First time you press the hotkey, the app downloads the ~700 MB ASR model
   from [Hugging Face](https://huggingface.co/Garnet-Owl/nemo-voice-typing-asr)
   into `%LOCALAPPDATA%\VoiceTyping\models\`. Takes a minute on a normal
   connection.

To auto-start with Windows: **right-click the tray icon → Start with Windows**.

## Use

| Action | How |
|---|---|
| Toggle dictation | **Ctrl+Alt+A** (or click the mic on the pill) |
| Hide the pill | Right-click pill → Hide |
| Show the pill | Left-click tray icon |
| Quit | Right-click tray icon → Exit |

The pill stays on top so you can drag it wherever. Position is remembered.
Hotkey is configurable in `%APPDATA%\VoiceTyping\config.json`.

## System requirements

- Windows 10 / 11, x64 (tested on Windows 11)
- ~1.5 GB RAM while dictating (model + ONNX runtime)
- Any modern x86-64 CPU with AVX2 — no GPU needed
- .NET 8 runtime (download prompt on first launch if missing)
- ~12 MB for the app, ~700 MB for the cached model

## Build

```powershell
git clone https://github.com/Garnet-Owl/nemo-voice-typing
cd nemo-voice-typing
dotnet publish src/VoiceTyping/VoiceTyping.csproj -c Release -r win-x64 `
    --self-contained false -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

The single-file exe lands in `dist\VoiceTyping.exe`.

## Stack

- WPF on .NET 8
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) for the tray
- [NAudio](https://github.com/naudio/NAudio) for mic capture
- [Microsoft.ML.OnnxRuntime](https://onnxruntime.ai/) for the streaming RNN-T inference
- Win32 `RegisterHotKey` + `SendInput` for global hotkey + text injection

The whole app is a few hundred lines of C# — see `src/VoiceTyping/`.

## Credits

Uses [`Garnet-Owl/nemo-voice-typing-asr`](https://huggingface.co/Garnet-Owl/nemo-voice-typing-asr),
a redistribution of the NVIDIA NeMo `nemotron-speech-streaming-en-0.6b` model
(INT4 ONNX) bundled with Silero VAD. See that repo for licenses.

## License

MIT (this repo's code). Model license is separate — see the HF model card.
