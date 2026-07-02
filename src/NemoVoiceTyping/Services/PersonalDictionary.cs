using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NemoVoiceTyping.Services;

/// <summary>
/// On-device, per-user vocabulary personalization for the ASR pipeline.
///
/// This is intentionally a lightweight, post-decode text correction layer
/// rather than true acoustic-model adaptation: the shipped model is a
/// quantized ONNX export with a plain greedy decoder (no beam search / LM
/// rescoring hook to bias), so nudging recognition would require offline
/// fine-tuning of the original NeMo checkpoint. Instead we catch the two
/// most common failure modes a non-US/UK accent or a personal/technical
/// vocabulary run into:
///
/// * <b>Corrections</b> — exact "the model always writes X when I say Y"
///   overrides (e.g. "onix" → "ONNX").
/// * <b>Hotwords</b> — a list of words/names you use a lot (proper nouns,
///   places, jargon) that recognized words get fuzzy-matched against, so a
///   near-miss spelling gets pulled to the correct one (e.g. "nairobbi" →
///   "Nairobi").
///
/// Matching is plain bounded Levenshtein distance over a small in-memory
/// list — no embeddings/vector search. For dictionaries in the tens-to-low-
/// hundreds of entries this is both faster and more precise than semantic
/// similarity, which is the wrong tool for "close misspelling of a known
/// word" and would add real latency to a real-time dictation path.
///
/// Storage is a JSON file on disk, deliberately kept OUT of the app's
/// install/source tree and living next to the model cache
/// (%LOCALAPPDATA%\NemoVoiceTyping\personalization\dictionary.json) so it's
/// per-machine, survives app updates, and is never committed to source
/// control.
/// </summary>
public sealed class PersonalDictionary
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _corrections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _hotwords = new();
    private DateTime _lastLoadUtc = DateTime.MinValue;

    // Voice-command keywords must never be "corrected" into a hotword —
    // doing so would silently break "scratch that", "new line", etc.
    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "period", "fullstop", "dot", "comma", "colon", "semicolon",
        "scratch", "delete", "new", "question", "exclamation",
        "that", "last", "line", "paragraph", "mark", "point",
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NemoVoiceTyping", "personalization", "dictionary.json");

    public string FilePath { get; }

    public PersonalDictionary(string? path = null)
    {
        FilePath = path ?? DefaultPath;
        Load();
    }

    /// <summary>Reloads from disk if the file changed since the last load
    /// (cheap timestamp check — call at the start of every dictation
    /// session so hand-edits to the JSON take effect without an app
    /// restart).</summary>
    public void ReloadIfChanged()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var mtime = File.GetLastWriteTimeUtc(FilePath);
            if (mtime > _lastLoadUtc) Load();
        }
        catch { /* best-effort */ }
    }

    private void Load()
    {
        lock (_lock)
        {
            _corrections.Clear();
            _hotwords.Clear();
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<DictionaryFile>(json);
                    if (data?.Corrections != null)
                        foreach (var kv in data.Corrections)
                            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                                _corrections[kv.Key.Trim()] = kv.Value.Trim();
                    if (data?.Hotwords != null)
                        foreach (var w in data.Hotwords)
                            if (!string.IsNullOrWhiteSpace(w))
                                _hotwords.Add(w.Trim());
                    _lastLoadUtc = File.GetLastWriteTimeUtc(FilePath);
                }
            }
            catch { /* corrupt/missing file just means no personalization yet */ }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var data = new DictionaryFile
                {
                    Hotwords = new List<string>(_hotwords),
                    Corrections = new Dictionary<string, string>(_corrections, StringComparer.OrdinalIgnoreCase),
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                _lastLoadUtc = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>Creates the dictionary file with a friendly starter template
    /// if it doesn't exist yet, so opening it in a text editor is
    /// self-explanatory.</summary>
    public void EnsureFileExists()
    {
        if (File.Exists(FilePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var template =
                "{\n" +
                "  \"_readme\": \"On-device personal vocabulary for Nemo Voice Typing. Lives only on this PC — never committed to source. 'hotwords': proper nouns/places/jargon you say a lot; near-miss spellings get auto-corrected to the exact spelling here. 'corrections': exact 'wrong heard -> right meant' overrides for words the model consistently gets wrong. Edits take effect the next time you start dictating.\",\n" +
                "  \"hotwords\": [],\n" +
                "  \"corrections\": {}\n" +
                "}\n";
            File.WriteAllText(FilePath, template);
        }
        catch { /* best-effort */ }
    }

    public void AddHotword(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        lock (_lock)
        {
            foreach (var w in _hotwords)
                if (string.Equals(w, word, StringComparison.OrdinalIgnoreCase)) return;
            _hotwords.Add(word.Trim());
        }
        Save();
    }

    public void AddCorrection(string wrong, string right)
    {
        if (string.IsNullOrWhiteSpace(wrong) || string.IsNullOrWhiteSpace(right)) return;
        lock (_lock) _corrections[wrong.Trim()] = right.Trim();
        Save();
    }

    public void RemoveHotword(string word)
    {
        lock (_lock)
            _hotwords.RemoveAll(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void RemoveCorrection(string wrong)
    {
        lock (_lock) _corrections.Remove(wrong);
        Save();
    }

    /// <summary>Snapshot of the current hotwords, for display in a UI.</summary>
    public List<string> GetHotwords()
    {
        lock (_lock) return new List<string>(_hotwords);
    }

    /// <summary>Snapshot of the current corrections (wrong → right), for
    /// display in a UI.</summary>
    public List<KeyValuePair<string, string>> GetCorrections()
    {
        lock (_lock) return new List<KeyValuePair<string, string>>(_corrections);
    }

    /// <summary>
    /// Attempts to correct a single plain word (no surrounding punctuation).
    /// Returns true and sets <paramref name="result"/> if a correction was
    /// applied; otherwise returns false and leaves <paramref name="result"/>
    /// equal to <paramref name="word"/>.
    /// </summary>
    public bool TryCorrect(string word, out string result)
    {
        result = word;
        if (string.IsNullOrEmpty(word) || ReservedWords.Contains(word)) return false;

        lock (_lock)
        {
            // 1. Exact explicit correction always wins.
            if (_corrections.TryGetValue(word, out var mapped))
            {
                result = ApplyCase(word, mapped);
                return !string.Equals(result, word, StringComparison.Ordinal);
            }

            if (_hotwords.Count == 0) return false;

            // 2. Already an exact (case-insensitive) hotword: leave as-is.
            foreach (var hw in _hotwords)
                if (string.Equals(hw, word, StringComparison.OrdinalIgnoreCase)) return false;

            // 3. Fuzzy match: bounded edit distance, length-gated so we
            // don't waste cycles comparing wildly different-length words.
            string? best = null;
            int bestDist = int.MaxValue;
            foreach (var hw in _hotwords)
            {
                int lenDiff = Math.Abs(hw.Length - word.Length);
                int threshold = Threshold(hw.Length);
                if (lenDiff > threshold) continue;

                int dist = BoundedLevenshtein(word, hw, threshold);
                if (dist >= 0 && dist < bestDist)
                {
                    bestDist = dist;
                    best = hw;
                    if (dist == 0) break;
                }
            }

            if (best != null)
            {
                result = ApplyCase(word, best);
                return true;
            }
        }
        return false;
    }

    // Roughly: allow 1 edit for short words, 2 for medium, 3 for long ones.
    // Keeps false-positive corrections rare while still catching the kind
    // of near-miss spelling an ASR model produces for an unfamiliar accent.
    private static int Threshold(int len) => len <= 4 ? 1 : len <= 8 ? 2 : 3;

    /// <summary>Levenshtein distance, early-exiting once it's clear the
    /// result will exceed <paramref name="maxDist"/>. Returns -1 if it
    /// would exceed the bound.</summary>
    private static int BoundedLevenshtein(string a, string b, int maxDist)
    {
        int la = a.Length, lb = b.Length;
        if (Math.Abs(la - lb) > maxDist) return -1;

        var prev = new int[lb + 1];
        var curr = new int[lb + 1];
        for (int j = 0; j <= lb; j++) prev[j] = j;

        for (int i = 1; i <= la; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            char ca = char.ToLowerInvariant(a[i - 1]);
            for (int j = 1; j <= lb; j++)
            {
                int cost = ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            if (rowMin > maxDist) return -1; // whole row exceeds bound, no point continuing
            (prev, curr) = (curr, prev);
        }
        return prev[lb] <= maxDist ? prev[lb] : -1;
    }

    /// <summary>Match the correction's letter-casing to how the original
    /// word was cased (all-caps / capitalized), unless the target already
    /// defines a deliberate mixed case (e.g. "NeMo", "ONNX") — in that case
    /// respect the dictionary's own casing.</summary>
    private static string ApplyCase(string original, string target)
    {
        // If the target has an uppercase letter anywhere after the first
        // position (e.g. "NeMo", "ONNX" has none-after-first but is all
        // caps, "iPhone"-style), treat that as a deliberate spelling and
        // don't touch it — the dictionary author knows best.
        bool targetHasInnerUpper = false;
        for (int i = 1; i < target.Length; i++)
            if (char.IsUpper(target[i])) { targetHasInnerUpper = true; break; }
        if (targetHasInnerUpper) return target;

        bool originalAllUpper = original.Length > 1;
        bool originalCapitalized = original.Length > 0 && char.IsUpper(original[0]);
        foreach (var c in original)
            if (char.IsLetter(c) && !char.IsUpper(c)) { originalAllUpper = false; break; }

        if (originalAllUpper) return target.ToUpperInvariant();
        if (originalCapitalized && target.Length > 0)
            return char.ToUpperInvariant(target[0]) + target.Substring(1);
        return target;
    }

    private sealed class DictionaryFile
    {
        [JsonPropertyName("hotwords")]
        public List<string>? Hotwords { get; set; }

        [JsonPropertyName("corrections")]
        public Dictionary<string, string>? Corrections { get; set; }
    }
}
