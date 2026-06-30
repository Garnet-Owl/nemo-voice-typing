using System;
using System.IO;
using System.Text;

namespace VoiceTyping.Services;

/// <summary>
/// SentencePiece-style vocab loader for the NeMo nemotron model.
/// Token IDs are line numbers (0-indexed). Pieces starting with U+2581 ("▁")
/// indicate a word boundary; detokenization replaces them with a leading space.
/// </summary>
public sealed class Tokenizer
{
    private readonly string[] _pieces;
    public int VocabSize => _pieces.Length;

    public Tokenizer(string vocabPath)
    {
        _pieces = File.ReadAllLines(vocabPath, Encoding.UTF8);
    }

    public string Detokenize(ReadOnlySpan<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if ((uint)id >= (uint)_pieces.Length) continue;
            var p = _pieces[id];
            if (p.Length > 0 && p[0] == '\u2581')
            {
                sb.Append(' ');
                sb.Append(p.AsSpan(1));
            }
            else
            {
                sb.Append(p);
            }
        }
        return sb.ToString();
    }

    public string Piece(int id) => (uint)id < (uint)_pieces.Length ? _pieces[id] : "";
}
