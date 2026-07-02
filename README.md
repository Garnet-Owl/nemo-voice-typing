# Nemo Voice Typing

![Nemo Voice Typing](src/NemoVoiceTyping/Assets/app.ico)

A small Windows tray app for dictating into any focused text field.
Press **Ctrl+Alt+A**, speak, and the words appear where your cursor is.
Speech recognition runs locally on the CPU using NVIDIA's NeMo streaming
model. After the first run it works offline.

## Why I built it

I'm a machine learning engineer. Most of my "typing" is prompting LLMs
and agents, which is a lot of long-form text, and my hands were starting
to hurt. Windows' built-in voice typing is fine, but I wanted something
that sits in the corner of my screen, starts on a chord I can hit with
one hand, and ships with a real model. No cloud round-trips and no
telemetry.

If you want a tool that lets you dictate anywhere, anytime on any window,
whether searching, typing chats, or anything else on your desktop, and it 
works really well, keeping your hands off the keyboard: I built it in one evening of 
tinkering, here it is.

## Install

1. Grab `Nemo Voice Typing.exe` from
   [Releases](https://github.com/Garnet-Owl/nemo-voice-typing/releases),
   or build from source (see below).
2. Double-click to run. A small pill appears on the right edge of the
   screen and a microphone icon shows up in the system tray.
3. The first time you press the hotkey the app downloads the ~700 MB ASR
   model from [Hugging Face](https://huggingface.co/Garnet-Owl/nemo-voice-typing-asr)
   into `%LOCALAPPDATA%\NemoVoiceTyping\models\`. Takes about a minute on
   a normal connection.

To auto-start with Windows, right-click the tray icon and pick **Start
with Windows**.

## Use

| Action | How |
|---|---|
| Toggle dictation | **Ctrl+Alt+A**, or click the mic on the pill |
| Hide the pill | Right-click pill, then Hide |
| Show the pill | Left-click the tray icon |
| Quit | Right-click the tray icon, then Exit |

The pill stays on top so you can drag it wherever. Position is
remembered between launches. Hotkey is configurable in
`%APPDATA%\NemoVoiceTyping\config.json`.

* **Auto-off on silence** — like Windows' own voice typing, the mic only
  listens while you're actually using it: if it doesn't recognize any
  speech for about 30 seconds, dictation stops on its own so it isn't
  burning CPU (or picking up other audio) after you've moved on. Just
  click the mic or hit the hotkey again to resume.

## Demo

[![Nemo Voice Typing Demo](https://img.youtube.com/vi/k6MXKX_60T0/maxresdefault.jpg)](https://www.youtube.com/watch?v=k6MXKX_60T0)

### Voice commands

While dictating you can say any of these and they'll be acted on instead
of typed verbatim. Spoken punctuation works in case the model itself
misses a mark.

| Say | What happens |
|---|---|
| "period" / "full stop" / "dot" | Inserts `.` |
| "comma" | Inserts `,` |
| "colon" / "semicolon" | Inserts `:` or `;` |
| "question mark" | Inserts `?` |
| "exclamation mark" / "exclamation point" | Inserts `!` |
| "new line" | Inserts a line break |
| "new paragraph" | Inserts a blank line |
| "delete last" | Deletes the last word |
| "scratch that" / "delete that" | Deletes the last sentence |

Punctuation from the model itself is always respected. Capitalization
at sentence starts is automatic, and mixed-case words like `iPhone`
are left alone.

## System requirements

* Windows 10 or 11, x64 (tested on Windows 11)
* RAM usage is around 800 MB (model plus ONNX runtime)
* Any modern x86-64 CPU with AVX2. No GPU needed.
* .NET 8 desktop runtime (Windows prompts to install it on first launch
  if missing).
* About 12 MB for the app and 700 MB for the cached model.

## Build

```powershell
git clone https://github.com/Garnet-Owl/nemo-voice-typing
cd nemo-voice-typing
dotnet publish src/NemoVoiceTyping/NemoVoiceTyping.csproj -c Release -r win-x64 `
    --self-contained false -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

The single-file exe lands in `dist\Nemo Voice Typing.exe`.

## Stack

* WPF on .NET 8
* [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) for the tray
* [NAudio](https://github.com/naudio/NAudio) for microphone capture
* [Microsoft.ML.OnnxRuntime](https://onnxruntime.ai/) for the streaming RNN-T inference
* Win32 `RegisterHotKey` and `SendInput` for the global hotkey and text injection

The whole app is a few hundred lines of C#. See `src/NemoVoiceTyping/`.

## Credits

Uses [`Garnet-Owl/nemo-voice-typing-asr`](https://huggingface.co/Garnet-Owl/nemo-voice-typing-asr),
a redistribution of the NVIDIA NeMo `nemotron-speech-streaming-en-0.6b`
model (INT4 ONNX) bundled with Silero VAD. See that repo for the model
licenses.

## License

MIT for this repo's code. The model has its own license, covered on the
Hugging Face page.
