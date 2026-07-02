using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using NemoVoiceTyping.Services;

namespace NemoVoiceTyping;

/// <summary>
/// Simple point-and-click editor for the on-device <see cref="PersonalDictionary"/>.
/// Deliberately not JSON — add a word or a "heard → meant" pair, see the
/// list update immediately, click ✕ to remove. No file format to learn.
/// </summary>
public partial class PersonalDictionaryWindow : Window
{
    private readonly PersonalDictionary _dictionary;
    private readonly ObservableCollection<string> _hotwords = new();
    private readonly ObservableCollection<CorrectionEntry> _corrections = new();

    public PersonalDictionaryWindow(PersonalDictionary dictionary)
    {
        _dictionary = dictionary;
        InitializeComponent();
        HotwordList.ItemsSource = _hotwords;
        CorrectionList.ItemsSource = _corrections;
        Reload();
    }

    private void Reload()
    {
        _hotwords.Clear();
        foreach (var w in _dictionary.GetHotwords()) _hotwords.Add(w);

        _corrections.Clear();
        foreach (var kv in _dictionary.GetCorrections()) _corrections.Add(new CorrectionEntry(kv.Key, kv.Value));
    }

    private void OnAddHotword(object sender, RoutedEventArgs e) => AddHotword();

    private void OnHotwordInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddHotword();
    }

    private void AddHotword()
    {
        var word = HotwordInput.Text.Trim();
        if (word.Length == 0) return;
        _dictionary.AddHotword(word);
        HotwordInput.Clear();
        HotwordInput.Focus();
        Reload();
    }

    private void OnRemoveHotword(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string word })
        {
            _dictionary.RemoveHotword(word);
            Reload();
        }
    }

    private void OnAddCorrection(object sender, RoutedEventArgs e) => AddCorrection();

    private void OnCorrectionInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddCorrection();
    }

    private void AddCorrection()
    {
        var wrong = HeardInput.Text.Trim();
        var right = CorrectInput.Text.Trim();
        if (wrong.Length == 0 || right.Length == 0) return;
        _dictionary.AddCorrection(wrong, right);
        HeardInput.Clear();
        CorrectInput.Clear();
        HeardInput.Focus();
        Reload();
    }

    private void OnRemoveCorrection(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string wrong })
        {
            _dictionary.RemoveCorrection(wrong);
            Reload();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed class CorrectionEntry
    {
        public string Wrong { get; }
        public string Right { get; }
        public CorrectionEntry(string wrong, string right) { Wrong = wrong; Right = right; }
    }
}
