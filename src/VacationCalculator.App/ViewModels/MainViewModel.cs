using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VacationCalculator.App.Services;
using VacationCalculator.Core;

namespace VacationCalculator.App.ViewModels;

/// <summary>A booked period as shown for the selected year.</summary>
public sealed record EntryRow(BookedPeriod Period, bool IsPast)
{
    public bool IsFeriefri => Period.Type == DayType.Feriefri;

    /// <summary>A stored period entirely on public holidays: it is listed but counts for nothing.</summary>
    public bool IsNotCounted => Period.Amount == 0;

    /// <summary>Muted tag after the description of a not-counted period; empty for normal rows. The rows are rebuilt when the language changes.</summary>
    public string NotCountedSuffix =>
        !IsNotCounted ? "" : Period.Description.Length > 0 ? $"  ({Loc.Get("Entry.NotCounted")})" : Loc.Get("Entry.NotCounted");

    /// <summary>The whole description cell as one text, for the tooltip when a long text is trimmed.</summary>
    public string? FullDescription => (Period.Description + NotCountedSuffix).Trim() is { Length: > 0 } text ? text : null;
}

/// <summary>One line of the sidebar's public-holiday list.</summary>
public sealed record HolidayRow(PublicHoliday Holiday)
{
    public DateOnly Date => Holiday.Date;
    public bool IsAutomatic => Holiday.IsAutomatic;

    /// <summary>Only the user's own entries can be removed; the Danish holidays are always there.</summary>
    public bool CanRemove => Holiday.IsManual;

    /// <summary>The holiday's name in the current language; empty for an entry of the user's own. The rows are rebuilt when the language changes.</summary>
    public string Name => Holiday.Kind is { } kind ? Loc.HolidayName(kind) : "";
    public bool HasName => Name.Length > 0;
    public string RemoveTooltip => Loc.Get(Holiday.IsAutomatic ? "Holidays.RemoveExtra" : "Holidays.Remove");
}

/// <summary>One line of the Feriefri card.</summary>
public sealed class FeriefriRow
{
    private readonly Action<decimal> _setAllowance;

    /// <param name="started">Whether the holiday year has started (today is on or after 1 September of its start year).</param>
    /// <param name="setAllowance">Stores a new allowance for this row's holiday year (saves; may refuse).</param>
    /// <param name="clearAllowance">Removes this row's own allowance.</param>
    public FeriefriRow(FeriefriBalance b, bool started, Action<decimal> setAllowance, Action clearAllowance)
    {
        _setAllowance = setAllowance;
        var a = b.Allowance;
        Name = b.HolidayYear.ToString();
        AllowanceDaysValue = b.AllowanceDays;
        AllowanceIsOwn = a.IsOwn;
        AllowanceSource = a.IsOwn ? Loc.Get("Feriefri.SourceOwn")
            : a.From is { } from ? Loc.Format("Feriefri.SourceFrom", from.ToString())
            : Loc.Get("Feriefri.SourceDefault");
        AllowanceTooltip = a.IsOwn ? Loc.Get("Feriefri.TooltipOwn")
            : a.From is { } f ? Loc.Format("Feriefri.TooltipInherited", f.ToString())
            : Loc.Get("Feriefri.TooltipDefault");
        Used = b.UsedDays;
        BookedAhead = b.BookedAheadDays;
        Remaining = b.RemainingDays;
        RemainingSign = Math.Sign(b.RemainingDays);
        UsedText = Loc.Format("Feriefri.Used", Loc.Num(b.UsedDays));
        BookedAheadText = Loc.Format("Feriefri.BookedAhead", Loc.Num(b.BookedAheadDays));
        RemainingText = Loc.Format("Feriefri.Remaining", Loc.Num(b.RemainingDays));
        ResetAllowanceCommand = new RelayCommand(_ => clearAllowance());

        // Year-end payout/lost notes only appear once the holiday year has started; before that they are just noise.
        var notes = new List<string>();
        if (b.ExceedsAllowanceBy > 0)
            notes.Add(Loc.Format("Feriefri.MoreThanAllowance", Loc.Num(b.ExceedsAllowanceBy)));
        if (started)
        {
            if (b.PaidOutAtFirstYearEnd > 0)
                notes.Add(Loc.Tense("Fate.PaidOutJan", b.PaidOutAtFirstYearEnd, b.FirstYearEndPassed,
                    Loc.Num(b.PaidOutAtFirstYearEnd), b.HolidayYear.StartYear + 1));
            if (b.PaidOutAtDeadline > 0)
                notes.Add(Loc.Tense("Fate.PaidOutJan", b.PaidOutAtDeadline, b.DeadlinePassed,
                    Loc.Num(b.PaidOutAtDeadline), b.HolidayYear.StartYear + 2));
            if (b.LostAtDeadline > 0)
                notes.Add(Loc.Tense("Fate.Lost", b.LostAtDeadline, b.DeadlinePassed, Loc.Num(b.LostAtDeadline)));
        }
        Note = string.Join("; ", notes);
        NoteIsError = b.ExceedsAllowanceBy > 0;
    }

    public string Name { get; }
    private decimal AllowanceDaysValue { get; }

    /// <summary>The allowance of this row's holiday year; writing it saves it for that year.</summary>
    public decimal AllowanceDays
    {
        get => AllowanceDaysValue;
        set
        {
            if (AllowanceIsOwn && value == AllowanceDaysValue)
                return;
            _setAllowance(value);
        }
    }

    public bool AllowanceIsOwn { get; }
    public string AllowanceSource { get; }
    public string AllowanceTooltip { get; }
    public RelayCommand ResetAllowanceCommand { get; }
    public decimal Used { get; }
    public decimal BookedAhead { get; }
    public decimal Remaining { get; }
    public string UsedText { get; }
    public string BookedAheadText { get; }
    public string RemainingText { get; }
    public int RemainingSign { get; }
    public string Note { get; }

    /// <summary>The note as a tooltip; null (no tooltip at all) when there is no note.</summary>
    public string? NoteTooltip => Note.Length > 0 ? Note : null;
    public bool NoteIsError { get; }
}

/// <summary>One line of the holiday-years card.</summary>
public sealed record HolidayYearRow(
    string Name, string Earned, decimal TransferredIn, decimal TransferredOut, decimal Used, decimal BookedAhead,
    decimal Remaining, int RemainingSign, DateOnly Deadline, string Note, bool NoteIsError,
    bool TransferredInIsProjected = false, string ProjectedTooltip = "", string ProjectedText = "")
{
    /// <summary>The transferred-in figure is a projection that is still counted in the previous holiday year.</summary>
    public bool ShowProjected => TransferredInIsProjected && TransferredIn > 0;

    public static HolidayYearRow From(HolidayYearBalance b)
    {
        var note = b.ExceedsEarnableBy > 0
            ? Loc.Format("Note.ExceedsEarnable", Loc.Num(b.ExceedsEarnableBy))
            : b.ExceedsEarnedSoFarBy > 0 ? Loc.Format("Note.Advance", Loc.Num(b.ExceedsEarnedSoFarBy)) : "";
        return new HolidayYearRow(
            b.HolidayYear.ToString(), Loc.Format("Table.Earned", Loc.Num(b.EarnedSoFar), Loc.Num(b.EarnableDays)), b.TransferredIn,
            b.TransferredToNext, b.UsedDays, b.BookedAheadDays, b.RemainingDays, Math.Sign(b.RemainingDays), b.Deadline, note,
            b.ExceedsEarnableBy > 0, b.TransferredInIsProjected,
            Loc.Format("Table.ProjectedTooltip", b.HolidayYear.Previous.ToString(), DateText.Long(b.HolidayYear.Previous.Deadline)),
            Loc.Format("Table.Projected", Loc.Num(b.TransferredIn)));
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly DataStore _store;
    private VacationData _data;
    private YearSettings _settings;
    private CalendarYearStatus _status;
    private IReadOnlyList<HolidayYearRow> _holidayYearRows = [];
    private IReadOnlyList<FeriefriRow> _feriefriRows = [];
    private bool _bookFeriefri;
    private CalendarYear _calendar;
    private IReadOnlyList<MonthView> _calendarMonths = [];
    private bool _isListTab;
    private bool _summaryExpanded = true;

    private DateOnly? _lastClicked;

    private DateTime? _newStart;
    private DateTime? _newEnd;
    private string _newDescription = "";
    private bool _isHalfDay;
    private DateTime? _newHoliday;
    private string _errorMessage = "";
    private string _infoMessage = "";

    private readonly Func<DateOnly> _today;
    private readonly ThemeService? _theme;

    public MainViewModel(DataStore store, Func<DateOnly>? today = null, ThemeService? theme = null)
    {
        _store = store;
        _theme = theme;
        // Without a preferences service the text is English, never the language of the machine the code runs on.
        Loc.SetLanguage(_theme?.Language ?? AppLanguage.English);
        LanguageOptions = Enum.GetValues<LanguageMode>().Select(m => new LanguageOption(m)).ToList();
        if (_theme is not null)
        {
            _theme.Changed += (_, _) => OnThemeChanged();
            _theme.LanguageChanged += (_, _) => OnLanguageChanged();
        }
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
        _data = store.Load();
        _settings = GetOrCreateSettings(ClampYear(Today.Year));
        _status = BalanceCalculator.CalculateCalendarYear(_data, _settings.Year, Today);
        _calendar = CalendarYear.Build(_data, _settings.Year, Today);

        PreviousYearCommand = new RelayCommand(_ => SelectedYear--, _ => SelectedYear > MinYear);
        NextYearCommand = new RelayCommand(_ => SelectedYear++, _ => SelectedYear < MaxYear);
        CurrentYearCommand = new RelayCommand(_ => SelectedYear = Today.Year);
        ToggleSummaryCommand = new RelayCommand(_ => IsSummaryExpanded = !IsSummaryExpanded);
        AddEntryCommand = new RelayCommand(_ => AddEntry());
        DeleteEntryCommand = new RelayCommand(p => DeleteEntry((EntryRow)p!));
        AddHolidayCommand = new RelayCommand(_ => AddHoliday());
        RemoveHolidayCommand = new RelayCommand(p => RemoveHoliday((DateOnly)p!));
        RemoveConflictDaysCommand = new RelayCommand(_ => RemoveConflictDays());
        CalendarGestureCommand = new RelayCommand(p => HandleCalendarGesture((CalendarGesture)p!));
        CalendarPreviewCommand = new RelayCommand(p => ShowDragPreview(p as CalendarGesture));
        ResetTransferLimitCommand = new RelayCommand(_ => Apply(() => _data.ClearTransferLimit(DeadlineYear)));
        ResetPayoutCommand = new RelayCommand(_ => Apply(() => _data.ClearPayout(DeadlineYear)));

        Refresh();
    }

    private DateOnly Today => _today();

    public string DataPath => _store.Path;

    /// <summary>The footer line "Saved to ..." in the current language.</summary>
    public string DataPathText => Loc.Format("Footer.SavedTo", DataPath);

    // Theme choice: three radio buttons bind to these; only a "true" write changes the mode.
    public bool IsSystemTheme
    {
        get => _theme is null || _theme.Mode == ThemeMode.System;
        set { if (value) SetThemeMode(ThemeMode.System); }
    }

    public bool IsLightTheme
    {
        get => _theme?.Mode == ThemeMode.Light;
        set { if (value) SetThemeMode(ThemeMode.Light); }
    }

    public bool IsDarkTheme
    {
        get => _theme?.Mode == ThemeMode.Dark;
        set { if (value) SetThemeMode(ThemeMode.Dark); }
    }

    /// <summary>Whether the stat cards and holiday-year tables are shown; remembered between sessions (not in the vacation data).</summary>
    public bool IsSummaryExpanded
    {
        get => _theme?.SummaryExpanded ?? _summaryExpanded;
        set
        {
            if (value == IsSummaryExpanded)
                return;

            _summaryExpanded = value;
            if (_theme is not null)
                _theme.SummaryExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryToggleText));
        }
    }

    public string SummaryToggleText => Loc.Get(IsSummaryExpanded ? "Summary.Hide" : "Summary.Show");

    /// <summary>The languages the picker offers (System first); each is shown in its own name.</summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>The picked language. Writing it applies at once to all text, clears the messages and saves only the preferences file.</summary>
    public LanguageOption SelectedLanguage
    {
        get => LanguageOptions.First(o => o.Mode == (_theme?.LanguageMode ?? LanguageMode.System));
        set
        {
            if (value is not null && _theme is not null)
                _theme.LanguageMode = value.Mode;
        }
    }

    /// <summary>Rebuilds every computed text in the new language. The vacation data is not touched or saved.</summary>
    private void OnLanguageChanged()
    {
        Loc.SetLanguage(_theme!.Language);
        ErrorMessage = "";
        InfoMessage = "";
        foreach (var option in LanguageOptions)
            option.Refresh();
        Refresh();
        // An empty name means "every property changed": all computed texts are read again.
        OnPropertyChanged(string.Empty);
    }

    private void SetThemeMode(ThemeMode mode)
    {
        if (_theme is not null)
            _theme.Mode = mode;
    }

    private void OnThemeChanged()
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }
    public RelayCommand CurrentYearCommand { get; }
    public RelayCommand ToggleSummaryCommand { get; }
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand DeleteEntryCommand { get; }
    public RelayCommand AddHolidayCommand { get; }
    public RelayCommand RemoveHolidayCommand { get; }
    public RelayCommand RemoveConflictDaysCommand { get; }
    /// <summary>A finished mouse gesture on the calendar (parameter: <see cref="ViewModels.CalendarGesture"/>).</summary>
    public RelayCommand CalendarGestureCommand { get; }
    /// <summary>Shows (or, with a null parameter, clears) the highlight of the range being dragged.</summary>
    public RelayCommand CalendarPreviewCommand { get; }
    public RelayCommand ResetTransferLimitCommand { get; }
    public RelayCommand ResetPayoutCommand { get; }

    public ObservableCollection<EntryRow> Entries { get; } = [];
    /// <summary>The user's own (manual) holiday dates of the selected year.</summary>
    public ObservableCollection<DateOnly> Holidays { get; } = [];

    /// <summary>All public holidays of the selected year for the sidebar list: Danish (automatic) and manual.</summary>
    public ObservableCollection<HolidayRow> PublicHolidays { get; } = [];

    /// <summary>The user's own holidays of the selected year (including ones that coincide with a Danish holiday), shown first.</summary>
    public ObservableCollection<HolidayRow> ManualHolidayRows { get; } = [];

    /// <summary>The Danish (automatic) holidays of the selected year.</summary>
    public ObservableCollection<HolidayRow> AutomaticHolidayRows { get; } = [];

    public bool HasNoManualHolidays => ManualHolidayRows.Count == 0;

    /// <summary>Note about booked days that fall on public holidays of the selected year and are not counted; empty when none.</summary>
    public string ConflictNote
    {
        get
        {
            var days = _data.BookedDaysOnHolidays(_settings.Year);
            if (days.Count == 0)
                return "";

            var dates = string.Join(", ", days.Select(d => DateText.Long(d.Date)));
            return Loc.Plural("Conflict.Note", days.Count, dates);
        }
    }

    public bool HasConflictNote => ConflictNote.Length > 0;

    /// <summary>Earliest selectable calendar year: it involves holiday years Y-1 and Y, both of which Core must support.</summary>
    public const int MinYear = HolidayYear.MinStartYear + 1;

    /// <summary>Latest selectable calendar year: holiday year Y (start year) is the later one involved.</summary>
    public const int MaxYear = HolidayYear.MaxStartYear;

    private static int ClampYear(int year) => Math.Clamp(year, MinYear, MaxYear);

    public int SelectedYear
    {
        get => _settings.Year;
        set
        {
            value = ClampYear(value);
            if (value == _settings.Year)
                return;

            _settings = GetOrCreateSettings(value);
            _lastClicked = null;
            ErrorMessage = "";
            InfoMessage = "";
            OnPropertyChanged();
            PreviousYearCommand.RaiseCanExecuteChanged();
            NextYearCommand.RaiseCanExecuteChanged();
            Refresh();
            ShowDragPreview(null);
        }
    }

    public DateTime? EmployedSince
    {
        get => _data.EmployedSince is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
        set
        {
            var date = value is { } v ? DateOnly.FromDateTime(v) : (DateOnly?)null;
            if (date == _data.EmployedSince)
                return;

            Apply(() => _data.EmployedSince = date);
        }
    }

    /// <summary>Holiday years that can be chosen as the first tracked one, oldest first.</summary>
    public IReadOnlyList<HolidayYear> FirstTrackedChoices
    {
        get
        {
            var newest = HolidayYear.OpenOn(Today)[^1].StartYear;
            var oldest = Math.Min(newest, Math.Max(MinFirstTrackedStartYear,
                _data.EmployedSince is { } since ? HolidayYear.Containing(since).StartYear : MinFirstTrackedStartYear));
            var years = Enumerable.Range(oldest, newest - oldest + 1).Select(y => new HolidayYear(y)).ToList();
            // A stored value outside that range (hand-edited, or clamped by Core) still has to be selectable: add just that year.
            var effective = _data.EffectiveFirstTrackedHolidayYear(Today);
            if (!years.Contains(effective))
            {
                if (effective.StartYear < oldest)
                    years.Insert(0, effective);
                else
                    years.Add(effective);
            }
            return years;
        }
    }

    private const int MinFirstTrackedStartYear = 2015;

    /// <summary>The first holiday year whose transfers are calculated; earlier years transfer nothing.</summary>
    public HolidayYear FirstTrackedHolidayYear
    {
        get => _data.EffectiveFirstTrackedHolidayYear(Today);
        set
        {
            if (value.StartYear == 0 || value == _data.EffectiveFirstTrackedHolidayYear(Today))
                return;

            Apply(() => _data.FirstTrackedHolidayYear = value.StartYear);
        }
    }

    /// <summary>Unused days brought into the first tracked holiday year.</summary>
    public decimal OpeningBalanceDays
    {
        get => _data.OpeningBalanceDays;
        set
        {
            if (value == _data.OpeningBalanceDays)
                return;

            Apply(() => _data.OpeningBalanceDays = value);
        }
    }

    private HolidayYear DeadlineYear => Status.HolidayYear.HolidayYear;
    private DeadlineSettings DeadlineSettings => Status.HolidayYear.Settings;

    /// <summary>Heading of the per-holiday-year settings in the sidebar.</summary>
    public string DeadlineSettingsTitle => Loc.Format("Settings.Title", DeadlineYear.ToString(), DateText.Long(DeadlineYear.Deadline));

    public decimal TransferLimitDays
    {
        get => DeadlineSettings.TransferLimitDays;
        set
        {
            if (DeadlineSettings.TransferLimitIsOwn && value == DeadlineSettings.TransferLimitDays)
                return;

            Apply(() => _data.SetTransferLimit(DeadlineYear, value));
        }
    }

    public string TransferLimitSource => Source(DeadlineSettings.TransferLimitFrom, DeadlineSettings.TransferLimitIsOwn);
    public bool TransferLimitIsOwn => DeadlineSettings.TransferLimitIsOwn;

    public bool PayoutNone
    {
        get => DeadlineSettings.PayoutMode == PayoutMode.None;
        set { if (value) SetPayoutMode(PayoutMode.None); }
    }

    public bool PayoutSome
    {
        get => DeadlineSettings.PayoutMode == PayoutMode.Some;
        set { if (value) SetPayoutMode(PayoutMode.Some); }
    }

    public bool PayoutAll
    {
        get => DeadlineSettings.PayoutMode == PayoutMode.All;
        set { if (value) SetPayoutMode(PayoutMode.All); }
    }

    public decimal PayoutDays
    {
        get => DeadlineSettings.PayoutDays;
        set
        {
            if (DeadlineSettings.PayoutMode != PayoutMode.Some || (DeadlineSettings.PayoutIsOwn && value == DeadlineSettings.PayoutDays))
                return;

            Apply(() => _data.SetPayout(DeadlineYear, PayoutMode.Some, value));
        }
    }

    public string PayoutSource => Source(DeadlineSettings.PayoutFrom, DeadlineSettings.PayoutIsOwn);
    public bool PayoutIsOwn => DeadlineSettings.PayoutIsOwn;

    private void SetPayoutMode(PayoutMode mode)
    {
        if (mode == DeadlineSettings.PayoutMode)
            return;

        var days = DeadlineSettings.PayoutDays;
        Apply(() => _data.SetPayout(DeadlineYear, mode, days));
    }

    private static string Source(HolidayYear? from, bool isOwn) =>
        isOwn ? Loc.Get("Settings.Source.Own") : from is { } year ? Loc.Format("Settings.Source.From", year.ToString()) : Loc.Get("Settings.Source.Default");

    private void RaiseDeadlineSettingsChanged()
    {
        foreach (var name in new[]
        {
            nameof(DeadlineSettingsTitle), nameof(TransferLimitDays), nameof(TransferLimitSource), nameof(TransferLimitIsOwn),
            nameof(PayoutNone), nameof(PayoutSome), nameof(PayoutAll), nameof(PayoutDays), nameof(PayoutSource), nameof(PayoutIsOwn),
        })
            OnPropertyChanged(name);
    }

    public CalendarYearStatus Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HolidayYearTitle));
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(StatusDetail));
                OnPropertyChanged(nameof(HasStatusDetail));
                OnPropertyChanged(nameof(UnbookedSign));
                OnPropertyChanged(nameof(EarnableNote));
            }
        }
    }
    /// <summary>The selected year laid out by Core for the calendar tab; rebuilt on every refresh.</summary>
    public CalendarYear Calendar
    {
        get => _calendar;
        private set
        {
            if (!SetField(ref _calendar, value))
                return;

            // Keep the month and cell objects and update them in place: replacing them makes the UI rebuild ~550 elements.
            if (_calendarMonths.Count == value.Months.Count)
            {
                for (var i = 0; i < _calendarMonths.Count; i++)
                    _calendarMonths[i].Update(value.Months[i]);
            }
            else
                CalendarMonths = value.Months.Select(m => new MonthView(m)).ToList();
        }
    }

    /// <summary>The twelve month cards of <see cref="Calendar"/> for display; the same objects are updated on every refresh.</summary>
    public IReadOnlyList<MonthView> CalendarMonths
    {
        get => _calendarMonths;
        private set => SetField(ref _calendarMonths, value);
    }

    /// <summary>Whether the List tab is shown instead of the Calendar tab; the Calendar tab is always the start tab.</summary>
    public bool IsListTab
    {
        get => _isListTab;
        set
        {
            if (SetField(ref _isListTab, value))
                OnPropertyChanged(nameof(IsCalendarTab));
        }
    }

    public bool IsCalendarTab
    {
        get => !_isListTab;
        set { if (value) IsListTab = false; }
    }

    /// <summary>The holiday years open around the selected calendar year (the expiring one and the next).</summary>
    public IReadOnlyList<HolidayYearRow> HolidayYearRows
    {
        get => _holidayYearRows;
        private set => SetField(ref _holidayYearRows, value);
    }

    /// <summary>Feriefri balances of the same holiday years as <see cref="HolidayYearRows"/>.</summary>
    public IReadOnlyList<FeriefriRow> FeriefriRows
    {
        get => _feriefriRows;
        private set => SetField(ref _feriefriRows, value);
    }

    /// <summary>Extra banner line about Feriefri days that are paid out or lost at 31 December; empty when none.</summary>
    public string FeriefriBanner
    {
        get
        {
            var e = FeriefriCalculator.CalculateYearEnd(_data, _settings.Year, Today);
            if (e.ExpiringDays <= 0)
                return "";

            var deadline = DateText.Long(e.Date);
            var paid = e.PaidOutDays;
            var lost = e.LostDays;
            var sentences = new List<string>();
            // The first sentence is introduced by "Feriefri:"; a second one continues it.
            string Lead(string sentence) => Loc.Format("Banner.Feriefri.Lead", sentence);
            if (e.IsProjected)
            {
                if (paid > 0)
                    sentences.Add(Lead(Loc.Number("Banner.Feriefri.MustUse", paid, Loc.Days(paid), deadline)));
                if (lost > 0)
                {
                    var sentence = paid > 0
                        ? Loc.Number("Banner.Feriefri.LostIfNotUsedThen", lost, Loc.Days(lost), e.Previous.ToString())
                        : Lead(Loc.Number("Banner.Feriefri.LostIfNotUsedBy", lost, Loc.Days(lost), e.Previous.ToString(), deadline));
                    sentences.Add(sentence);
                }
            }
            else
            {
                if (paid > 0)
                    sentences.Add(Lead(Loc.Tense("Fate.PaidOutJan", paid, true, Loc.Days(paid), e.Year + 1) + "."));
                if (lost > 0)
                {
                    var sentence = Loc.Tense("Banner.Feriefri.LostFrom", lost, true, Loc.Days(lost), e.Previous.ToString());
                    sentences.Add(paid > 0 ? sentence : Lead(sentence));
                }
            }
            return string.Join(" ", sentences);
        }
    }

    public bool HasFeriefriBanner => FeriefriBanner.Length > 0;

    public string HolidayYearTitle =>
        Loc.Format("Summary.HolidayYearTitle", Status.HolidayYear.HolidayYear.ToString(), DateText.Long(new DateOnly(Status.Year, 12, 31)));

    public string EarnableNote => Loc.Format("Summary.EarnableNote", Loc.Num(Status.HolidayYear.EarnableDays));

    /// <summary>Sign (-1, 0, 1) of the not-yet-booked days; the "Not yet booked" figure is coloured by it.</summary>
    public int UnbookedSign => Math.Sign(Status.UnbookedDays);

    public string StatusMessage
    {
        get
        {
            var s = Status;
            var deadline = DateText.Long(new DateOnly(s.Year, 12, 31));
            var unbooked = s.UnbookedDays;
            // The free-day count starts today in the current year, and covers the whole year for a future one.
            var window = s.Year > Today.Year
                ? Loc.Format("Status.Window.InYear", s.Year, deadline)
                : Loc.Format("Status.Window.FromToday", deadline);
            return s.Status switch
            {
                BalanceStatus.AllPlanned when s.HolidayYear.TotalDays == 0 =>
                    Loc.Format("Status.NoDays", s.HolidayYear.HolidayYear.ToString()),
                BalanceStatus.AllPlanned => Loc.Get(PastDeadline ? "Status.NothingLost.Past" : "Status.NothingLost.Future"),
                BalanceStatus.DaysToPlan =>
                    Loc.Number("Status.DaysToPlan", unbooked, Loc.Days(unbooked), deadline, Loc.Plural("Unit.StillFree", s.WorkingDaysLeft)),
                BalanceStatus.DaysWillBeLost =>
                    Loc.Format("Status.WillBeLost", Loc.Days(unbooked), Loc.Plural("Unit.Left", s.WorkingDaysLeft), window),
                BalanceStatus.OverBooked =>
                    Loc.Format("Status.OverBooked", Loc.Days(s.HolidayYear.ExceedsEarnableBy)),
                BalanceStatus.Expired =>
                    Loc.Format("Status.Expired", s.HolidayYear.HolidayYear.ToString(), deadline,
                        Outcome(s.HolidayYear.AtDeadline, s.NextHolidayYear, past: true)),
                _ => "",
            };
        }
    }

    private bool PastDeadline => Status.Deadline < Today;

    /// <summary>Second, smaller line of the banner: what happens to the days at the deadline.</summary>
    public string StatusDetail
    {
        get
        {
            var s = Status;
            var lines = new List<string>();
            var next = s.NextHolidayYear;
            var transferredIn = s.HolidayYear.UnusedTransferredInDays;
            switch (s.Status)
            {
                case BalanceStatus.DaysToPlan:
                    var outcome = Outcome(s.HolidayYear.AtDeadline, next, past: false);
                    if (outcome.Length > 0)
                        lines.Add(Loc.Format("Detail.IfNotBooked", outcome));
                    break;
                case BalanceStatus.DaysWillBeLost:
                    lines.Add(Loc.Format("Detail.CantFit", Loc.Days(s.DaysAtRisk), Outcome(s.AtRisk, next, past: false)));
                    break;
                case BalanceStatus.AllPlanned when s.HolidayYear.TotalDays > 0 && s.TransferredToNext > 0:
                    lines.Add(Loc.Tense("Fate.Transferred", s.TransferredToNext, PastDeadline, Loc.Days(s.TransferredToNext), next.ToString()) + ".");
                    break;
            }

            if (transferredIn > 0 && s.Status is BalanceStatus.DaysToPlan or BalanceStatus.DaysWillBeLost)
            {
                lines.Add(s.HolidayYear.HolidayYear == _data.EffectiveFirstTrackedHolidayYear(Today)
                    ? Loc.Number("Detail.OpeningMustUse", transferredIn, Loc.Days(transferredIn))
                    : Loc.Number("Detail.TransferredMustUse", transferredIn, Loc.Days(transferredIn), s.HolidayYear.HolidayYear.Previous.ToString()));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>"5 days will be transferred to 2026/27, 2 days will be paid out and 3 days will be lost" (zero parts omitted).</summary>
    private static string Outcome(DeadlineOutcome o, HolidayYear next, bool past)
    {
        var parts = new List<string>();
        if (o.TransferredDays > 0)
            parts.Add(Loc.Tense("Fate.Transferred", o.TransferredDays, past, Loc.Days(o.TransferredDays), next.ToString()));
        if (o.PaidOutDays > 0)
            parts.Add(Loc.Tense("Fate.PaidOut", o.PaidOutDays, past, Loc.Days(o.PaidOutDays)));
        if (o.LostDays > 0)
            parts.Add(Loc.Tense("Fate.Lost", o.LostDays, past, Loc.Days(o.LostDays)));
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => Loc.Format("List.And", string.Join(", ", parts.Take(parts.Count - 1)), parts[^1]),
        };
    }
    public bool HasStatusDetail => StatusDetail.Length > 0;
    public DateTime? NewStart
    {
        get => _newStart;
        set
        {
            if (!SetField(ref _newStart, value))
                return;

            // Default to a single-day booking, and keep the range valid.
            if (value is not null && (NewEnd is null || NewEnd < value))
                NewEnd = value;
            UpdateHalfDayAvailability();
            OnPropertyChanged(nameof(NewEntryPreview));
        }
    }

    public DateTime? NewEnd
    {
        get => _newEnd;
        set
        {
            if (!SetField(ref _newEnd, value))
                return;

            UpdateHalfDayAvailability();
            OnPropertyChanged(nameof(NewEntryPreview));
        }
    }

    /// <summary>A half day can only be booked for a single date.</summary>
    public bool CanBookHalfDay => NewStart is { } start && NewEnd is { } end && start.Date == end.Date;

    public bool IsHalfDay
    {
        get => _isHalfDay;
        set
        {
            if (SetField(ref _isHalfDay, value))
                OnPropertyChanged(nameof(NewEntryPreview));
        }
    }

    private void UpdateHalfDayAvailability()
    {
        OnPropertyChanged(nameof(CanBookHalfDay));
        if (!CanBookHalfDay)
            IsHalfDay = false;
    }

    /// <summary>Whether the booking form books Feriefri days instead of vacation.</summary>
    public bool BookFeriefri
    {
        get => _bookFeriefri;
        set
        {
            if (!SetField(ref _bookFeriefri, value))
                return;

            OnPropertyChanged(nameof(BookVacation));
            OnPropertyChanged(nameof(AddButtonText));
        }
    }

    public bool BookVacation
    {
        get => !_bookFeriefri;
        set { if (value) BookFeriefri = false; }
    }

    public string AddButtonText => Loc.Get(_bookFeriefri ? "Form.AddFeriefri" : "Form.AddVacation");

    public string NewDescription
    {
        get => _newDescription;
        set => SetField(ref _newDescription, value);
    }

    public string NewEntryPreview
    {
        get
        {
            if (NewStart is not { } startDate || NewEnd is not { } endDate || endDate < startDate)
                return "";

            var start = DateOnly.FromDateTime(startDate);
            var end = DateOnly.FromDateTime(endDate);
            // Check the limit first so a huge range is never counted.
            return Bookings.IsRangeWithinLimit(start, end) ? Loc.Plural("Unit.WorkingDay", PreviewDays(start, end)) : Loc.Get("Preview.TooLong");
        }
    }

    private decimal PreviewDays(DateOnly start, DateOnly end)
    {
        var days = CountWorkingDays(start, end);
        return IsHalfDay && days > 0 ? 0.5m : days;
    }

    public DateTime? NewHoliday
    {
        get => _newHoliday;
        set => SetField(ref _newHoliday, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>Neutral note about something that was saved (partial booking, half-day conversion); empty when none.</summary>
    public string InfoMessage
    {
        get => _infoMessage;
        private set => SetField(ref _infoMessage, value);
    }

    private void AddEntry()
    {
        ErrorMessage = "";
        InfoMessage = "";
        if (NewStart is not { } startDate || NewEnd is not { } endDate)
        {
            ErrorMessage = Loc.Get("Error.PickDates");
            return;
        }

        var start = DateOnly.FromDateTime(startDate);
        var end = DateOnly.FromDateTime(endDate);
        if (end < start)
        {
            ErrorMessage = Loc.Get("Error.EndBeforeStart");
            return;
        }
        if (!Bookings.IsRangeWithinLimit(start, end))
        {
            ErrorMessage = TooLongMessage(start, Bookings.LatestAllowedEnd(start));
            return;
        }
        if (CountWorkingDays(start, end) == 0)
        {
            ErrorMessage = start == end && _data.FindPublicHoliday(start) is { } holiday
                ? Loc.UpperFirst(holiday.Kind is { } kind
                    ? Loc.Format("Error.PublicHolidayNamed", DateText.WithWeekday(start), Loc.HolidayName(kind))
                    : Loc.Format("Error.PublicHoliday", DateText.WithWeekday(start)))
                : Loc.Get("Error.NoWorkingDays");
            return;
        }

        var description = NewDescription.Trim();
        var halfDay = IsHalfDay && start == end;
        if (BookFeriefri)
        {
            AddFeriefri(start, end, description, halfDay);
            return;
        }

        // On a failed save the form stays filled so the booking can be retried.
        if (!Apply(() =>
            {
                var holidays = _data.AllPublicHolidays();
                _data.Days = halfDay
                    ? Bookings.BookHalfDay(_data.Days, start, DayType.Vacation, description, holidays)
                    : Bookings.BookRange(_data.Days, start, end, DayType.Vacation, description, holidays);
            }))
            return;

        IsHalfDay = false;
        NewStart = null;
        NewEnd = null;
        NewDescription = "";
    }

    private void AddFeriefri(DateOnly start, DateOnly end, string description, bool halfDay)
    {
        // BookFeriefri doesn't change _data; a refused booking saves nothing, a partial one saves what fitted.
        var result = Bookings.BookFeriefri(_data, start, end, halfDay ? BookedDay.HalfDay : BookedDay.FullDay, description);
        if (result.IsRefused)
        {
            ErrorMessage = RefusedMessage(result.Limit);
            return;
        }

        var alreadyBooked = result.RequestedDays == 0 && result.UnchangedDates.Count > 0;
        if (!alreadyBooked && !Apply(() => _data.Days = result.Days))
            return;

        IsHalfDay = false;
        NewStart = null;
        NewEnd = null;
        NewDescription = "";

        if (alreadyBooked)
        {
            InfoMessage = Loc.Get("Feriefri.AlreadyBooked");
            return;
        }
        InfoMessage = FeriefriPartialMessage(result.BookedDays, result.LeftOutDays, result.Limit, result.HalfDayDates);
    }

    /// <summary>Note for a Feriefri booking that didn't fit completely (shared by the form and the calendar); empty when it did.</summary>
    private static string FeriefriPartialMessage(decimal bookedDays, decimal leftOutDays, FeriefriLimit? limit, IEnumerable<DateOnly> halfDayDates)
    {
        var messages = new List<string>();
        if (leftOutDays > 0)
        {
            var head = Loc.Format("Feriefri.Partial.Head", Loc.FeriefriDays(bookedDays), Loc.Days(leftOutDays));
            if (limit is { PaidOutAtFirstYearEnd: > 0 } reserved)
            {
                var paid = reserved.PaidOutAtFirstYearEnd;
                var usable = reserved.AllowanceDays - paid;
                var yearEnd = DateText.Long(new DateOnly(reserved.HolidayYear.StartYear, 12, 31));
                messages.Add(Loc.Number("Feriefri.Partial.Reserved", paid, head, Loc.Num(usable), Loc.FeriefriDays(reserved.AllowanceDays),
                    reserved.HolidayYear.ToString(), Loc.Num(paid), reserved.HolidayYear.StartYear + 1, yearEnd));
            }
            else if (limit is not null)
                messages.Add(Loc.Format("Feriefri.Partial.UsedUpFor", head, limit.HolidayYear.ToString()));
            else
                messages.Add(Loc.Format("Feriefri.Partial.UsedUp", head));
        }
        foreach (var date in halfDayDates)
            messages.Add(Loc.Format("Feriefri.HalfDay", DateText.Long(date)));
        return string.Join(" ", messages);
    }

    private static string TooLongMessage(DateOnly start, DateOnly latestEnd) =>
        Loc.Format("Error.TooLong", DateText.Long(start), DateText.Long(latestEnd));

    /// <summary>A finished calendar gesture: a click cycles the day, a drag or Shift+click books a range.</summary>
    private void HandleCalendarGesture(CalendarGesture gesture)
    {
        ErrorMessage = "";
        InfoMessage = "";
        ShowDragPreview(null);

        var previous = _lastClicked;
        _lastClicked = gesture.Date;
        if (gesture.Anchor != gesture.Date)
            BookCalendarRange(gesture.Anchor, gesture.Date, gesture.Type);
        else if (gesture.Shift && previous is { } from)
            BookCalendarRange(from, gesture.Date, gesture.Type);
        else
            CycleCalendarDay(gesture.Date, gesture.Type);
    }

    private void CycleCalendarDay(DateOnly date, DayType type)
    {
        var result = Bookings.CycleDay(_data, date, type);
        if (result.Outcome == DayCycleOutcome.Refused)
        {
            ErrorMessage = RefusedMessage(result.Limit);
            return;
        }
        if (!result.Changed || !Apply(() => _data.Days = result.Days))
            return;

        if (result.Outcome is DayCycleOutcome.ConvertedToHalf or DayCycleOutcome.ReplacedAsHalf && result.Limit is { } limit)
            InfoMessage = Loc.Format("Feriefri.HalfLeft", limit.HolidayYear.ToString(), DateText.WithWeekday(date));
    }

    private void BookCalendarRange(DateOnly from, DateOnly to, DayType type)
    {
        var result = Bookings.BookTypedRange(_data, from, to, type);
        switch (result.Outcome)
        {
            case RangeBookingOutcome.TooLong:
                ErrorMessage = TooLongMessage(result.Start, result.LatestAllowedEnd ?? Bookings.LatestAllowedEnd(result.Start));
                return;
            case RangeBookingOutcome.Refused:
                ErrorMessage = RefusedMessage(result.Limit);
                return;
            case RangeBookingOutcome.NoWorkingDays:
                return;
        }

        // Every day already was what the range would book: nothing to save, no message.
        if (!result.Changed)
            return;

        if (Apply(() => _data.Days = result.Days) && result.Outcome == RangeBookingOutcome.Partial)
            InfoMessage = FeriefriPartialMessage(result.BookedDays, result.LeftOutDays, result.Limit, result.HalfDayDates);
    }

    /// <summary>Highlights the cells between the anchor and the pointer of a drag in progress; null clears it.</summary>
    private void ShowDragPreview(CalendarGesture? gesture)
    {
        var kind = gesture is null ? DragKind.None : gesture.Type == DayType.Feriefri ? DragKind.Feriefri : DragKind.Vacation;
        var (first, last) = gesture is null ? (DateOnly.MaxValue, DateOnly.MinValue)
            : gesture.Anchor <= gesture.Date ? (gesture.Anchor, gesture.Date) : (gesture.Date, gesture.Anchor);
        foreach (var month in _calendarMonths)
        {
            foreach (var cell in month.Cells.OfType<DayCell>())
                cell.Drag = cell.IsBookable && cell.Date >= first && cell.Date <= last ? kind : DragKind.None;
        }
    }

    private static string RefusedMessage(FeriefriLimit? limit)
    {
        if (limit is null)
            return Loc.Get("Feriefri.None");

        var all = Loc.FeriefriDays(limit.AllowanceDays);
        return limit.PaidOutAtFirstYearEnd > 0
            ? Loc.Format("Feriefri.AllUsedPaid", all, limit.HolidayYear.ToString(), Loc.Num(limit.PaidOutAtFirstYearEnd), limit.HolidayYear.StartYear + 1)
            : Loc.Format("Feriefri.AllUsed", all, limit.HolidayYear.ToString());
    }

    private void DeleteEntry(EntryRow row)
    {
        Apply(() => _data.Days = Bookings.RemoveDays(_data.Days, row.Period.Dates));
    }

    private void AddHoliday()
    {
        ErrorMessage = "";
        InfoMessage = "";
        if (NewHoliday is not { } value)
        {
            ErrorMessage = Loc.Get("Error.PickHoliday");
            return;
        }

        var date = DateOnly.FromDateTime(value);
        if (DanishHolidays.KindOf(date) is { } existing)
        {
            // Already a public holiday: nothing to store.
            NewHoliday = null;
            InfoMessage = Loc.Format("Info.AlreadyHoliday", DateText.Long(date), Loc.HolidayName(existing));
            return;
        }

        if (!Apply(() =>
            {
                var settings = GetOrCreateSettings(date.Year);
                if (!settings.PublicHolidays.Contains(date))
                {
                    settings.PublicHolidays.Add(date);
                    settings.PublicHolidays.Sort();
                }
            }))
            return;

        NewHoliday = null;
        if (date.Year != SelectedYear)
            InfoMessage = Loc.Format("Info.AddedToYear", date.Year);
    }

    /// <summary>Deletes the stored records on public holidays of the selected year; they were never counted, so balances stay.</summary>
    private void RemoveConflictDays()
    {
        var dates = _data.BookedDaysOnHolidays(_settings.Year).Select(d => d.Date).ToList();
        if (dates.Count == 0)
            return;

        if (Apply(() => _data.Days = Bookings.RemoveDays(_data.Days, dates)))
            InfoMessage = Loc.Plural("Info.Removed", dates.Count);
    }

    private void RemoveHoliday(DateOnly date)
    {
        Apply(() => _settings.PublicHolidays.Remove(date));
    }

    private YearSettings GetOrCreateSettings(int year)
    {
        var existing = _data.Years.Find(y => y.Year == year);
        if (existing is not null)
            return existing;

        var settings = new YearSettings { Year = year };
        _data.Years.Add(settings);
        return settings;
    }

    private int CountWorkingDays(DateOnly start, DateOnly end) =>
        BalanceCalculator.CountWorkingDays(start, end, _data.AllPublicHolidays());

    // Same JSON settings as DataStore, so a snapshot round-trips exactly what would be saved.
    private static readonly JsonSerializerOptions SnapshotOptions = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Applies a user change and saves it. If saving fails, the in-memory data is restored to what it was before the
    /// change, so the app always shows what is on disk. Returns whether the change was saved.
    /// </summary>
    private bool Apply(Action mutate)
    {
        var snapshot = JsonSerializer.Serialize(_data, SnapshotOptions);
        ErrorMessage = "";
        InfoMessage = "";
        try
        {
            mutate();
            _data.EnsureFirstTrackedHolidayYear(Today);
            _store.Save(_data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _data = JsonSerializer.Deserialize<VacationData>(snapshot, SnapshotOptions)!;
            _settings = GetOrCreateSettings(_settings.Year);
            Refresh();
            ErrorMessage = Loc.Format("Error.SaveFailed", ex.Message.TrimEnd('.', ' '));
            return false;
        }

        Refresh();
        return true;
    }

    private void Refresh()
    {
        Entries.Clear();
        foreach (var period in Bookings.GroupPeriods(_data.Days, _settings.Year, _data.AllPublicHolidays()))
            Entries.Add(new EntryRow(period, period.End <= Today));

        Holidays.Clear();
        foreach (var holiday in _settings.PublicHolidays.Order())
            Holidays.Add(holiday);

        PublicHolidays.Clear();
        foreach (var holiday in _data.PublicHolidaysIn(_settings.Year))
            PublicHolidays.Add(new HolidayRow(holiday));
        ManualHolidayRows.Clear();
        foreach (var row in PublicHolidays.Where(r => r.CanRemove))
            ManualHolidayRows.Add(row);
        AutomaticHolidayRows.Clear();
        foreach (var row in PublicHolidays.Where(r => r.IsAutomatic))
            AutomaticHolidayRows.Add(row);
        OnPropertyChanged(nameof(HasNoManualHolidays));
        OnPropertyChanged(nameof(ConflictNote));
        OnPropertyChanged(nameof(HasConflictNote));

        Status = BalanceCalculator.CalculateCalendarYear(_data, _settings.Year, Today);
        Calendar = CalendarYear.Build(_data, _settings.Year, Today);
        HolidayYearRows = BalanceCalculator.CalculateHolidayYears(_data, HolidayYear.InCalendarYear(_settings.Year), Today)
            .Select(HolidayYearRow.From).ToList();
        FeriefriRows = FeriefriCalculator.CalculateHolidayYears(_data, HolidayYear.InCalendarYear(_settings.Year), Today)
            .Select(b => new FeriefriRow(
                b, Today >= new DateOnly(b.HolidayYear.StartYear, 9, 1),
                days => Apply(() => _data.SetFeriefriAllowance(b.HolidayYear, days)),
                () => Apply(() => _data.ClearFeriefriAllowance(b.HolidayYear)))).ToList();
        OnPropertyChanged(nameof(FeriefriBanner));
        OnPropertyChanged(nameof(HasFeriefriBanner));
        OnPropertyChanged(nameof(EmployedSince));
        OnPropertyChanged(nameof(FirstTrackedChoices));
        OnPropertyChanged(nameof(FirstTrackedHolidayYear));
        OnPropertyChanged(nameof(OpeningBalanceDays));
        OnPropertyChanged(nameof(NewEntryPreview));
        RaiseDeadlineSettingsChanged();
    }

}
