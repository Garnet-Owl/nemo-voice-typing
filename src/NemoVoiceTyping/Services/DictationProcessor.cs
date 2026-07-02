using System;
using System.Collections.Generic;
using System.Text;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Word-level post-processor that sits between the streaming ASR and
/// <see cref="TextInjector"/>. It handles:
///
/// * Buffering sub-word pieces into whole words so we can inspect them
///   before sending them to the focused window
/// * Spoken punctuation: "period", "comma", "question mark",
///   "exclamation mark", "colon", "semicolon"
/// * Layout commands: "new line", "new paragraph"
/// * Undo commands: "scratch that" / "delete that" / "delete last"
///   (removes the previous sentence, or everything since dictation
///   started if no sentence boundary exists yet)
/// * Auto-capitalisation at the start of a sentence
/// * Auto-period after a long pause (about 800ms of silence with a
///   pending sentence — same threshold Dragon NaturallySpeaking ships
///   with and within the 700–1000ms range Google's Speech-to-Text API
///   uses for sentence boundaries; comma-on-pause is intentionally
///   skipped because in practice it depends on prosody more than on
///   pause length)
///
/// Everything is driven from a single worker thread. Call <see cref="Push"/>
/// on every emitted piece and <see cref="Tick"/> on a steady cadence so
/// the pause-based logic fires.
/// </summary>
public sealed class DictationProcessor
{
    private readonly StringBuilder _wordBuf = new();
    private readonly List<string> _emitted = new(); // each entry is the exact substring we typed
    private readonly PersonalDictionary? _dictionary;
    private DateTime _lastWordUtc = DateTime.MinValue;  // last FLUSHED word (drives auto-period)
    private DateTime _lastPieceUtc = DateTime.MinValue; // last sub-word piece arriving (drives buffer flush)
    private bool _sentenceStart = true;
    private string? _pendingCommand;       // first half of a two-word command, e.g. "scratch"
    private string? _pendingCommandTyped;  // what we already typed for it (so we can undo)
    private DateTime _pendingCommandUtc;

    // Research-backed sentence-boundary threshold: Google STT uses ~700ms,
    // Dragon NaturallySpeaking ~800ms. 800ms is the sweet spot that avoids
    // over-punctuating mid-thought pauses while still feeling responsive.
    private static readonly TimeSpan CommandWindow = TimeSpan.FromMilliseconds(1500);

    // Buffer-idle flush threshold. Sub-word pieces of a single word arrive
    // at chunk-completion boundaries (the model processes audio in 560ms
    // chunks per genai_config.json), so consecutive pieces of one word are
    // ≤560ms apart in practice. 1200ms gives us ~2 chunks of headroom for
    // CPU jitter while keeping end-of-utterance latency around 1s. The
    // model's own VAD endpoint (silence_duration_ms = 3360) is much longer
    // because it's deciding utterance boundaries, not word completion.
    private static readonly TimeSpan BufferIdleFlush = TimeSpan.FromMilliseconds(1200);

    public DictationProcessor(PersonalDictionary? dictionary = null)
    {
        _dictionary = dictionary;
    }

    public void FlushBuffer()
    {
        if (_wordBuf.Length > 0) FlushWord();
        if (_pendingCommand != null) ClearPending(commit: true);
    }

    public void Reset()
    {
        _wordBuf.Clear();
        _emitted.Clear();
        _sentenceStart = true;
        _pendingCommand = null;
        _pendingCommandTyped = null;
        _lastWordUtc = DateTime.MinValue;
        _lastPieceUtc = DateTime.MinValue;
    }

    /// <summary>Push a sub-word piece from the ASR.</summary>
    public void Push(string piece)
    {
        if (string.IsNullOrEmpty(piece)) return;
        bool boundary = piece[0] == '\u2581';
        string clean = boundary ? piece.Substring(1) : piece;

        if (boundary && _wordBuf.Length > 0)
            FlushWord();

        if (clean.Length > 0)
            _wordBuf.Append(clean);

        _lastPieceUtc = DateTime.UtcNow;
    }

    /// <summary>Called from a timer so pause-driven logic still fires.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        // Word still in the buffer but no NEW sub-word piece has arrived for
        // a while: flush whatever we have. Matched to the model's own VAD
        // (genai_config.json silence_duration_ms = 3360) so we never tear a
        // word mid-decode.
        if (_wordBuf.Length > 0 && now - _lastPieceUtc > BufferIdleFlush)
        {
            FlushWord();
        }
        // Pending half-command that never got its second word: emit it
        // verbatim so the user isn't left wondering.
        if (_pendingCommand != null && now - _pendingCommandUtc > CommandWindow)
        {
            ClearPending(commit: true);
        }
        // NB: no auto-period on pause. The streaming nemotron model emits
        // its own '. ? !' tokens based on learned prosody, and any second-
        // guessing on our side just produces wrong punctuation on natural
        // mid-thought pauses. If the model stays silent on punctuation,
        // the user can dictate "period" / "question mark" explicitly.
    }

    private void FlushWord()
    {
        string raw = _wordBuf.ToString();
        _wordBuf.Clear();
        if (raw.Length == 0) return;

        _lastWordUtc = DateTime.UtcNow;

        // If the model emitted a pure-punctuation "word" (just "?" or "!"),
        // treat it as the model's verdict on the previous sentence: if we
        // had already auto-appended a weaker mark (e.g. "."), upgrade it
        // rather than ending up with "sentence. ?". The model wins.
        if (raw.Length > 0 && raw.IndexOfAny(new[] { 'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z','A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z','0','1','2','3','4','5','6','7','8','9' }) < 0)
        {
            char mark = raw[0];
            if (mark is '?' or '!' or '.' or ',' or ';' or ':' && _emitted.Count > 0)
            {
                string prev = _emitted[_emitted.Count - 1];
                if (prev.Length > 0)
                {
                    char prevTail = prev[prev.Length - 1];
                    // Upgrade weaker auto-punctuation to model's stronger choice.
                    if (prevTail == '.' && mark is '?' or '!')
                    {
                        TextInjector.Backspace(1);
                        TextInjector.Type(mark.ToString());
                        _emitted[_emitted.Count - 1] = prev.Substring(0, prev.Length - 1) + mark;
                        _sentenceStart = true;
                        return;
                    }
                    // Same mark already there → swallow the duplicate.
                    if (prevTail == mark) return;
                }
            }
            AttachPunctuation(raw);
            return;
        }

        string lower = raw.ToLowerInvariant().Trim('.', ',', '?', '!', ';', ':');

        // --- two-word commands (second half arriving) -------------------
        if (_pendingCommand != null)
        {
            if (DateTime.UtcNow - _pendingCommandUtc <= CommandWindow)
            {
                if ((_pendingCommand == "scratch" || _pendingCommand == "delete") && lower == "that")
                {
                    // Undo "scratch"/"delete" that we already typed plus the
                    // most recent sentence (back to a terminal punctuation
                    // mark, or to the beginning of this dictation session).
                    ClearPending(commit: false);
                    DeleteLastSentence();
                    return;
                }
                if (_pendingCommand == "delete" && lower == "last")
                {
                    ClearPending(commit: false);
                    DeleteLastWord();
                    return;
                }
                if (_pendingCommand == "new" && lower == "line")
                {
                    ClearPending(commit: false);
                    InsertEnter(blankLines: 1);
                    return;
                }
                if (_pendingCommand == "new" && lower == "paragraph")
                {
                    ClearPending(commit: false);
                    InsertEnter(blankLines: 2);
                    return;
                }
                if (_pendingCommand == "question" && lower == "mark")
                {
                    ClearPending(commit: false);
                    AttachPunctuation("?");
                    return;
                }
                if (_pendingCommand == "exclamation" && (lower == "mark" || lower == "point"))
                {
                    ClearPending(commit: false);
                    AttachPunctuation("!");
                    return;
                }
            }
            // Second word didn't form a command: commit the pending word
            // (it's already on screen) and fall through to handle this word.
            ClearPending(commit: true);
        }

        // --- single-word punctuation -----------------------------------
        string? simple = lower switch
        {
            "period" or "fullstop" or "dot" => ".",
            "comma" => ",",
            "colon" => ":",
            "semicolon" => ";",
            _ => null,
        };
        if (simple != null)
        {
            AttachPunctuation(simple);
            return;
        }

        // --- start of two-word command (hold and watch) ----------------
        if (lower is "scratch" or "delete" or "new" or "question" or "exclamation")
        {
            // We still type it so the user sees feedback; if the second
            // word arrives in time we'll undo it.
            string typed = TypeWord(raw);
            _pendingCommand = lower;
            _pendingCommandTyped = typed;
            _pendingCommandUtc = DateTime.UtcNow;
            return;
        }

        // --- ordinary word --------------------------------------------
        TypeWord(ApplyPersonalDictionary(raw));
    }

    /// <summary>Runs the recognized word through the on-device personal
    /// dictionary (exact corrections + fuzzy hotword matching), preserving
    /// any trailing punctuation the model attached to the word.</summary>
    private string ApplyPersonalDictionary(string raw)
    {
        if (_dictionary == null) return raw;

        int end = raw.Length;
        while (end > 0 && ".,!?;:".IndexOf(raw[end - 1]) >= 0) end--;
        if (end == 0) return raw; // pure punctuation, nothing to correct

        string core = raw.Substring(0, end);
        string trailing = raw.Substring(end);
        return _dictionary.TryCorrect(core, out var corrected) ? corrected + trailing : raw;
    }

    /// <summary>Inserts the word into the focused window with leading space
    /// + capitalisation as appropriate. Returns the exact characters typed.</summary>
    private string TypeWord(string word)
    {
        var sb = new StringBuilder(word.Length + 2);
        bool needSpace = _emitted.Count > 0 && !_sentenceStart
                         && !LastEndsWithSoftBreak();
        if (_emitted.Count > 0 && _sentenceStart && !LastEndsWithHardBreak())
            sb.Append(' ');
        else if (needSpace)
            sb.Append(' ');

        // Auto-capitalise the start of a sentence — but ONLY when the model
        // wrote the word in all lowercase. If it deliberately emitted mixed
        // case (e.g. "iPhone", "eBay"), respect the model's choice.
        bool wordIsAllLower = true;
        for (int i = 0; i < word.Length; i++)
            if (char.IsUpper(word[i])) { wordIsAllLower = false; break; }
        if (_sentenceStart && wordIsAllLower && word.Length > 0 && char.IsLower(word[0]))
            sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
        else
            sb.Append(word);

        string text = sb.ToString();
        TextInjector.Type(text);
        _emitted.Add(text);
        // If the ASR model emitted a sentence-terminator inside this word
        // (e.g. "okay?"), trust it and flip into sentence-start mode so the
        // next word gets capitalised. The streaming nemotron model is trained
        // to predict ". ? !" tokens from prosody, and we want to honour them
        // rather than override with our 800ms pause heuristic.
        char tail = text[text.Length - 1];
        _sentenceStart = tail is '.' or '?' or '!';
        return text;
    }

    private void AttachPunctuation(string punct)
    {
        TextInjector.Type(punct);
        if (_emitted.Count > 0)
            _emitted[_emitted.Count - 1] += punct;
        else
            _emitted.Add(punct);

        if (punct is "." or "?" or "!")
            _sentenceStart = true;
    }

    private void InsertEnter(int blankLines)
    {
        for (int i = 0; i < blankLines; i++)
            TextInjector.PressEnter();
        _emitted.Add(new string('\n', blankLines));
        _sentenceStart = true;
    }

    private void DeleteLastSentence()
    {
        if (_emitted.Count == 0) return;
        int total = 0;
        // Walk backwards until we cross a sentence boundary.
        while (_emitted.Count > 0)
        {
            string seg = _emitted[_emitted.Count - 1];
            total += seg.Length;
            _emitted.RemoveAt(_emitted.Count - 1);
            if (_emitted.Count > 0)
            {
                string prev = _emitted[_emitted.Count - 1];
                if (prev.Length > 0)
                {
                    char c = prev[prev.Length - 1];
                    if (c == '.' || c == '?' || c == '!') break;
                }
            }
        }
        TextInjector.Backspace(total);
        _sentenceStart = true;
    }

    private void DeleteLastWord()
    {
        if (_emitted.Count == 0) return;
        string seg = _emitted[_emitted.Count - 1];
        TextInjector.Backspace(seg.Length);
        _emitted.RemoveAt(_emitted.Count - 1);
        if (_emitted.Count == 0) _sentenceStart = true;
    }

    private void ClearPending(bool commit)
    {
        if (!commit && _pendingCommandTyped != null)
        {
            TextInjector.Backspace(_pendingCommandTyped.Length);
            if (_emitted.Count > 0 && _emitted[_emitted.Count - 1] == _pendingCommandTyped)
            {
                _emitted.RemoveAt(_emitted.Count - 1);
                if (_emitted.Count == 0) _sentenceStart = true;
                else
                {
                    var prev = _emitted[_emitted.Count - 1];
                    char c = prev[prev.Length - 1];
                    _sentenceStart = c is '.' or '?' or '!';
                }
            }
        }
        _pendingCommand = null;
        _pendingCommandTyped = null;
    }

    private bool LastEndsWithHardBreak()
    {
        if (_emitted.Count == 0) return true;
        string last = _emitted[_emitted.Count - 1];
        if (last.Length == 0) return false;
        char c = last[last.Length - 1];
        return c == '\n';
    }

    private bool LastEndsWithSoftBreak() => LastEndsWithHardBreak();
}
