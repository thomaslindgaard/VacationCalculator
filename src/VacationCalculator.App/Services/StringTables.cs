namespace VacationCalculator.App.Services;

/// <summary>
/// The string tables, one per language, with identical keys and the same <c>{0}</c> placeholders (a unit test checks both).
/// <para>
/// To add a string: add the key to <c>StringTables.English.cs</c>, then to the Danish, Romanian and Redneck files, with the
/// same placeholders; use it as <c>{svc:Str Group.Name}</c> in XAML or <c>Loc.Get</c>/<c>Loc.Format</c> in code. The app name
/// "Vacation Calculator" is never in a table. Numbers go in already formatted (<see cref="Loc.Num"/>), dates through
/// <see cref="DateText"/>. Plural keys come in three forms (<c>.One</c>, <c>.Few</c>, <c>.Other</c>; only Romanian tells
/// <c>.Few</c> apart), see <see cref="Loc.Plural"/>; verb-tense keys in <c>.Future/.Past</c> times <c>.One/.Other</c> (<see cref="Loc.Tense"/>);
/// agreement keys in <c>.One/.Other</c> (<see cref="Loc.Number"/>).
/// </para>
/// </summary>
public static partial class StringTables
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishTable = new(() => BuildEnglish());
    private static readonly Lazy<IReadOnlyDictionary<string, string>> DanishTable = new(() => BuildDanish());
    private static readonly Lazy<IReadOnlyDictionary<string, string>> RomanianTable = new(() => BuildRomanian());
    private static readonly Lazy<IReadOnlyDictionary<string, string>> RedneckTable = new(() => BuildRedneck());

    /// <summary>The table of a language: key to text.</summary>
    public static IReadOnlyDictionary<string, string> For(AppLanguage language) => language switch
    {
        AppLanguage.Danish => DanishTable.Value,
        AppLanguage.Romanian => RomanianTable.Value,
        AppLanguage.Redneck => RedneckTable.Value,
        _ => EnglishTable.Value,
    };
}
