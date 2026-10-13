using System.ComponentModel;

namespace AIlight;

/// <summary>Eine Zeile der Vorschlagsliste, wie die Oberfläche sie bindet.</summary>
public sealed class SuggestionRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public SuggestionRow(Suggestion item, bool showSeparator = false)
    {
        Item = item;
        ShowSeparator = showSeparator;
    }

    public Suggestion Item { get; }

    public string Glyph => Item.Glyph;

    public string Title => Item.Title;

    public string Subtitle => Item.Subtitle;

    /// <summary>Eine dünne Linie über dieser Zeile — markiert den Wechsel der Gruppe.</summary>
    public bool ShowSeparator { get; }

    /// <summary>Zeilen, die eine Frage abschicken, werden mit dem Akzent hervorgehoben.</summary>
    public bool IsAsk => Item.Kind == SuggestionKind.Ask;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
