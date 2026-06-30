# Nemo Voice Typing

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
telemetry. One evening of hacking, here it is.

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

## System requirements

* Windows 10 or 11, x64 (tested on Windows 11)
* About 1.5 GB of RAM while dictating (model plus ONNX runtime)
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
