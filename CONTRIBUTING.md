# Contributing

Thanks for wanting to help with Nemo Voice Typing. Bug fixes, performance
work, new platform support and small features are all welcome. This page
covers how to get set up and what a pull request needs before it can be
merged.

## Before you start

For a small fix, go ahead and open a pull request. For anything bigger
(a new feature, a new window or tray item, a change to how dictation
behaves) please open an issue first so we can agree on the approach
before you put time into it.

Keep each pull request to one change. Three focused PRs are much easier
to review than one that mixes a fix, a refactor and a feature.

## Setup

You need:

- Windows 10 or 11, x64 or ARM64
- The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- About 700 MB free for the speech model, which the app downloads from
  Hugging Face the first time you start dictation

Build and run:

```powershell
git clone https://github.com/Garnet-Owl/nemo-voice-typing
cd nemo-voice-typing
dotnet build src/NemoVoiceTyping -c Release
```

To produce the single-file exe that users actually run:

```powershell
dotnet publish src/NemoVoiceTyping/NemoVoiceTyping.csproj -c Release -r win-x64 `
    --self-contained false -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Use `-r win-arm64` and a different output folder for an ARM64 build. Close
the running app (tray icon, then Exit) before publishing, or the copy
fails because the exe is locked.

## Tests

```powershell
dotnet test tests/unit
dotnet test tests/integration
```

Unit tests live in `tests/unit` and use xUnit. The integration tests run
the real model against a recorded clip at `.var/samples/kenyan_en_sample.wav`.
That folder is gitignored, so without your own sample those tests skip
themselves. Any short 16 kHz or higher English WAV works if you want to
run them.

A lot of this app (the global hotkey, the tray, the mic, typing into other
windows) can only be checked by running it. Passing tests are not enough
on their own, so please also run the published exe and try your change
by hand.

## Code style

Look at the code around your change and match it. In particular:

- File-scoped namespaces, nullable reference types on.
- One class per concern under `src/NemoVoiceTyping/Services/`. UI logic
  stays in the window code-behind.
- Components talk through C# events (`event Action<T>`), and they are
  wired together in `App.xaml.cs`.
- P/Invoke declarations stay private inside the class that uses them.
- Audio and inference run off the UI thread. Update the UI through
  `Dispatcher`.
- The app runs all day, so avoid allocations on hot paths such as the
  audio callback, the inference loop and per-frame timers. Reuse buffers
  and prefer `Span<T>`.
- Name things clearly instead of explaining them in comments. Use an XML
  doc comment (`/// <summary>`) only when there is a constraint the code
  can't show on its own. Please don't add comments that describe what a
  line does or the history of a change.

## Changelog

If your change is something a user would notice, add an entry at the top
of `CHANGELOG.md`. The newest section is copied word for word into the
GitHub release notes, and some readers aren't technical, so:

- Use a heading like `## 2026-09-27 — Short summary of the change`.
- Describe what changed for the user and why it matters. Don't mention
  file names, classes or other internals.
- At most four bullets. If there is already an entry for the same day,
  fold your change into it instead of adding more bullets.
- Purely internal work (tests, refactors, CI, docs) doesn't need an entry.

## Commits and pull requests

Commit messages start with a type, matching the existing history:
`feat:`, `fix:`, `perf:`, `refactor:`, `build:`, `docs:` or `test:`.

In the pull request description, say what problem you're fixing, how you
fixed it and how you tested it. If you measured something (speed, memory,
accuracy), include the numbers.

Before you ask for a review, check that:

- `dotnet build src/NemoVoiceTyping -c Release` succeeds with no new warnings
- `dotnet test tests/unit` passes
- you ran the published app and tried the change by hand
- `CHANGELOG.md` has an entry, if users will notice the change

AI-assisted contributions are fine. You are still responsible for
understanding the change and for having tested it.

## Releases

Merging to `master` does not publish a release. The maintainer runs the
release workflow by hand from the Actions tab once a batch of changes is
ready.

## Reporting security problems

Please don't open a public issue for a security problem. Use the
**Report a vulnerability** button on the repository's Security tab
instead, so it can be fixed before it's widely known.

## License

By contributing, you agree that your contribution is released under the
same MIT license as the rest of the code in this repository.
