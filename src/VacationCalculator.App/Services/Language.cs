using System.Globalization;

namespace VacationCalculator.App.Services;

/// <summary>A language the interface is written in.</summary>
public enum AppLanguage
{
    English,
    Danish,
    Romanian,
    /// <summary>A good-natured American Southern dialect of English (a novelty option; never chosen automatically).</summary>
    Redneck,
}

/// <summary>The user's language choice. <see cref="System"/> follows the Windows display language.</summary>
public enum LanguageMode
{
    System,
    English,
    Danish,
    Romanian,
    Redneck,
}

public static class Languages
{
    /// <summary>
    /// Resolves the choice to a language. System follows the Windows display (UI) language: Danish gives Danish,
    /// Romanian gives Romanian, anything else (and a missing culture) gives English. Redneck is only ever picked.
    /// </summary>
    public static AppLanguage Resolve(LanguageMode mode, CultureInfo? uiCulture) => mode switch
    {
        LanguageMode.English => AppLanguage.English,
        LanguageMode.Danish => AppLanguage.Danish,
        LanguageMode.Romanian => AppLanguage.Romanian,
        LanguageMode.Redneck => AppLanguage.Redneck,
        _ => uiCulture?.TwoLetterISOLanguageName switch
        {
            "da" => AppLanguage.Danish,
            "ro" => AppLanguage.Romanian,
            _ => AppLanguage.English,
        },
    };

    /// <summary>The language written in itself, as shown in the picker; null for System (which is translated).</summary>
    public static string? NativeName(LanguageMode mode) => mode switch
    {
        LanguageMode.English => "English",
        LanguageMode.Danish => "Dansk",
        LanguageMode.Romanian => "Română",
        LanguageMode.Redneck => "Redneck",
        _ => null,
    };
}
