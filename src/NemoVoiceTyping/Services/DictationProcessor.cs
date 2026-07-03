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
///
/// Everything is driven from a single worker thread. Call <see cref="Push"/>
/// on every emitted piece and <see cref="Tick"/> on a steady cadence so
/// the pause-based logic fires.
/// </summary>
public sealed class DictationProcessor
{
    private readonly StringBuilder _wordBuf = new();

    /// <summary>Each entry is the exact substring typed into the focused window.</summary>
    private readonly List<string> _emitted = new();

    private readonly PersonalDictionary? _dictionary;
    private DateTime _lastWordUtc = DateTime.MinValue;
    private DateTime _lastPieceUtc = DateTime.MinValue;
    private bool _sentenceStart = true;

    /// <summary>First half of a two-word command, e.g. "scratch".</summary>
    private string? _pendingCommand;

    /// <summary>What was typed for the pending half-command, so it can be undone.</summary>
    private string? _pendingCommandTyped;

    private DateTime _pendingCommandUtc;

    private static readonly TimeSpan CommandWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Sub-word pieces of one word arrive at most one model chunk (560ms per
    /// genai_config.json) apart; 1200ms gives ~2 chunks of headroom before
    /// the buffered word is flushed. Kept well under the model's own VAD
    /// endpoint (silence_duration_ms = 3360), which decides utterance
    /// boundaries, not word completion.
    /// </summary>
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
        bool boundary = piece[0] == '▁';
        string clean = boundary ? piece.Substring(1) : piece;

        if (boundary && _wordBuf.Length > 0)
            FlushWord();

        if (clean.Length > 0)
            _wordBuf.Append(clean);

        _lastPieceUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Called from a timer so pause-driven logic fires. Deliberately no
    /// auto-period on pause: the streaming nemotron model emits its own
    /// '. ? !' tokens from learned prosody, and second-guessing it produces
    /// wrong punctuation on natural mid-thought pauses.
    /// </summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (_wordBuf.Length > 0 && now - _lastPieceUtc > BufferIdleFlush)
        {
            FlushWord();
        }
        if (_pendingCommand != null && now - _pendingCommandUtc > CommandWindow)
        {
            ClearPending(commit: true);
        }
    }

    /// <summary>
    /// A pure-punctuation "word" from the model (just "?" or "!") is its
    /// verdict on the previous sentence: a weaker mark already typed (".")
    /// is upgraded rather than producing "sentence. ?", and an identical
    /// mark is swallowed as a duplicate.
    /// </summary>
    private void FlushWord()
    {
        string raw = _wordBuf.ToString();
        _wordBuf.Clear();
        if (raw.Length == 0) return;

        _lastWordUtc = DateTime.UtcNow;

        if (raw.Length > 0 && raw.IndexOfAny(new[] { 'a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z','A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P','Q','R','S','T','U','V','W','X','Y','Z','0','1','2','3','4','5','6','7','8','9' }) < 0)
        {
            char mark = raw[0];
            if (mark is '?' or '!' or '.' or ',' or ';' or ':' && _emitted.Count > 0)
            {
                string prev = _emitted[_emitted.Count - 1];
                if (prev.Length > 0)
                {
                    char prevTail = prev[prev.Length - 1];
                    if (prevTail == '.' && mark is '?' or '!')
                    {
                        TextInjector.Backspace(1);
                        TextInjector.Type(mark.ToString());
                        _emitted[_emitted.Count - 1] = prev.Substring(0, prev.Length - 1) + mark;
                        _sentenceStart = true;
                        return;
                    }
                    if (prevTail == mark) return;
                }
            }
            AttachPunctuation(raw);
            return;
        }

        string lower = raw.ToLowerInvariant().Trim('.', ',', '?', '!', ';', ':');

        if (_pendingCommand != null)
        {
            if (DateTime.UtcNow - _pendingCommandUtc <= CommandWindow)
            {
                if ((_pendingCommand == "scratch" || _pendingCommand == "delete") && lower == "that")
                {
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
            ClearPending(commit: true);
        }

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

        if (lower is "scratch" or "delete" or "new" or "question" or "exclamation")
        {
            string typed = TypeWord(raw);
            _pendingCommand = lower;
            _pendingCommandTyped = typed;
            _pendingCommandUtc = DateTime.UtcNow;
            return;
        }

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
        if (end == 0) return raw;

        string core = raw.Substring(0, end);
        string trailing = raw.Substring(end);
        return _dictionary.TryCorrect(core, out var corrected) ? corrected + trailing : raw;
    }

    /// <summary>
    /// Inserts the word into the focused window with leading space and
    /// capitalisation as appropriate; returns the exact characters typed.
    /// Sentence starts are capitalised only when the model emitted the word
    /// in all lowercase — deliberate mixed case ("iPhone") is respected.
    /// A sentence terminator inside the word ("okay?") is trusted and flips
    /// the next word into sentence-start mode.
    /// </summary>
    private string TypeWord(string word)
    {
        var sb = new StringBuilder(word.Length + 2);
        bool needSpace = _emitted.Count > 0 && !_sentenceStart
                         && !LastEndsWithSoftBreak();
        if (_emitted.Count > 0 && _sentenceStart && !LastEndsWithHardBreak())
            sb.Append(' ');
        else if (needSpace)
            sb.Append(' ');

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
