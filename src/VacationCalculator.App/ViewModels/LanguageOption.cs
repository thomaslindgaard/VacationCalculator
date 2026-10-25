using VacationCalculator.App.Services;

namespace VacationCalculator.App.ViewModels;

/// <summary>One entry of the language picker. Languages are named in themselves; "System" is translated.</summary>
public sealed class LanguageOption : ObservableObject
{
    public LanguageOption(LanguageMode mode) => Mode = mode;

    public LanguageMode Mode { get; }

    public string DisplayName => Languages.NativeName(Mode) ?? Loc.Get("Language.System");

    /// <summary>Screen readers (and UI automation) name the list item by this.</summary>
    public override string ToString() => DisplayName;

    /// <summary>Re-reads <see cref="DisplayName"/> after a language switch.</summary>
    public void Refresh() => OnPropertyChanged(nameof(DisplayName));
}
