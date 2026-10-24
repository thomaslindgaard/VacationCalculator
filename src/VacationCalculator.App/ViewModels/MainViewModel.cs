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

    public const string NotCountedTag = "public holiday – not counted";

    /// <summary>Muted tag after the description of a not-counted period; empty for normal rows.</summary>
    public string NotCountedSuffix => !IsNotCounted ? "" : Period.Description.Length > 0 ? $"  ({NotCountedTag})" : NotCountedTag;
}

/// <summary>One line of the sidebar's public-holiday list.</summary>
public sealed record HolidayRow(PublicHoliday Holiday)
{
    public DateOnly Date => Holiday.Date;
    public bool IsAutomatic => Holiday.IsAutomatic;

    /// <summary>Only the user's own entries can be removed; the Danish holidays are always there.</summary>
    public bool CanRemove => Holiday.IsManual;

    public string Name => Holiday.Name ?? "";
    public bool HasName => Name.Length > 0;
    public string RemoveTooltip => Holiday.IsAutomatic ? "Remove your extra entry (the public holiday stays)" : "Remove";
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
        AllowanceSource = a.IsOwn ? "own" : a.From is { } from ? $"from {from}" : "default";
        AllowanceTooltip = a.IsOwn ? "Set for this holiday year" : a.From is { } f ? $"Inherited from {f}" : "Default allowance";
        Used = b.UsedDays;
        BookedAhead = b.BookedAheadDays;
        Remaining = b.RemainingDays;
        RemainingSign = Math.Sign(b.RemainingDays);
        ResetAllowanceCommand = new RelayCommand(_ => clearAllowance());

        // Year-end payout/lost notes only appear once the holiday year has started; before that they are just noise.
        var notes = new List<string>();
        if (b.ExceedsAllowanceBy > 0)
            notes.Add($"{b.ExceedsAllowanceBy:0.##} more than the allowance");
        if (started)
        {
            if (b.PaidOutAtFirstYearEnd > 0)
                notes.Add($"{b.PaidOutAtFirstYearEnd:0.##} {Verb(b.PaidOutAtFirstYearEnd, b.FirstYearEndPassed)} paid out in January {b.HolidayYear.StartYear + 1}");
            if (b.PaidOutAtDeadline > 0)
                notes.Add($"{b.PaidOutAtDeadline:0.##} {Verb(b.PaidOutAtDeadline, b.DeadlinePassed)} paid out in January {b.HolidayYear.StartYear + 2}");
            if (b.LostAtDeadline > 0)
                notes.Add($"{b.LostAtDeadline:0.##} {Verb(b.LostAtDeadline, b.DeadlinePassed)} lost");
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
    public int RemainingSign { get; }
    public string Note { get; }
    public bool NoteIsError { get; }

    internal static string Verb(decimal days, bool past) => past ? (days == 1 ? "was" : "were") : "will be";
}

/// <summary>One line of the holiday-years card.</summary>
public sealed record HolidayYearRow(
    string Name, string Earned, decimal TransferredIn, decimal TransferredOut, decimal Used, decimal BookedAhead,
    decimal Remaining, int RemainingSign, DateOnly Deadline, string Note, bool NoteIsError,
    bool TransferredInIsProjected = false, string ProjectedTooltip = "")
{
    /// <summary>The transferred-in figure is a projection that is still counted in the previous holiday year.</summary>
    public bool ShowProjected => TransferredInIsProjected && TransferredIn > 0;

    public static HolidayYearRow From(HolidayYearBalance b)
    {
        var note = b.ExceedsEarnableBy > 0
            ? $"{b.ExceedsEarnableBy:0.##} more than can be earned by 31 August"
            : b.ExceedsEarnedSoFarBy > 0 ? $"advance holiday: {b.ExceedsEarnedSoFarBy:0.##} not earned yet" : "";
        return new HolidayYearRow(
            b.HolidayYear.ToString(), $"{b.EarnedSoFar:0.##} of {b.EarnableDays:0.##}", b.TransferredIn, b.TransferredToNext, b.UsedDays,
            b.BookedAheadDays, b.RemainingDays, Math.Sign(b.RemainingDays), b.Deadline, note, b.ExceedsEarnableBy > 0,
            b.TransferredInIsProjected,
            $"Projected transfer from {b.HolidayYear.Previous} on {DateText.Long(b.HolidayYear.Previous.Deadline)}. " +
            $"Until then these days still count as remaining in {b.HolidayYear.Previous}.");
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
        if (_theme is not null)
            _theme.Changed += (_, _) => OnThemeChanged();
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
        ResetTransferLimitCommand = new RelayCommand(_ => Apply(() => _data.ClearTransferLimit(DeadlineYear)));
        ResetPayoutCommand = new RelayCommand(_ => Apply(() => _data.ClearPayout(DeadlineYear)));

        Refresh();
    }

    private DateOnly Today => _today();

    public string DataPath => _store.Path;

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

    public string SummaryToggleText => IsSummaryExpanded ? "Hide summary" : "Show summary";

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
            return $"{(days.Count == 1 ? "1 booked day falls" : $"{days.Count} booked days fall")} on public holidays " +
                $"and {(days.Count == 1 ? "is" : "are")} not counted: {dates}.";
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
            ErrorMessage = "";
            InfoMessage = "";
            OnPropertyChanged();
            PreviousYearCommand.RaiseCanExecuteChanged();
            NextYearCommand.RaiseCanExecuteChanged();
            Refresh();
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
    public string DeadlineSettingsTitle => $"When {DeadlineYear} expires ({DateText.Long(DeadlineYear.Deadline)})";

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
        isOwn ? "Set for this year" : from is { } year ? $"From {year}" : "Default";

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
            if (e.IsProjected)
            {
                if (paid > 0)
                    sentences.Add($"Feriefri: {Days(paid)} must be used by {deadline}, otherwise {(paid == 1 ? "it is" : "they are")} paid out in January.");
                if (lost > 0)
                    sentences.Add($"{(paid > 0 ? "" : "Feriefri: ")}{Days(lost)} from {e.Previous} will be lost if not used by {(paid > 0 ? "then" : deadline)}.");
            }
            else
            {
                if (paid > 0)
                    sentences.Add($"Feriefri: {Days(paid)} {FeriefriRow.Verb(paid, true)} paid out in January {e.Year + 1}.");
                if (lost > 0)
                    sentences.Add($"{(paid > 0 ? "" : "Feriefri: ")}{Days(lost)} from {e.Previous} {FeriefriRow.Verb(lost, true)} lost.");
            }
            return string.Join(" ", sentences);
        }
    }

    public bool HasFeriefriBanner => FeriefriBanner.Length > 0;

    public string HolidayYearTitle =>
        $"Holiday year {Status.HolidayYear.HolidayYear} · use by {DateText.Long(new DateOnly(Status.Year, 12, 31))}";

    public string EarnableNote => $"of {Status.HolidayYear.EarnableDays:0.##} earnable";

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
                ? $"in {s.Year} up to and including {deadline}"
                : $"from today through {deadline}";
            return s.Status switch
            {
                BalanceStatus.AllPlanned when s.HolidayYear.TotalDays == 0 =>
                    $"No vacation days to use in holiday year {s.HolidayYear.HolidayYear}.",
                BalanceStatus.AllPlanned => PastDeadline ? "Nothing was paid out or lost." : "Nothing will be paid out or lost.",
                BalanceStatus.DaysToPlan =>
                    $"You have {Days(unbooked)} not yet booked. {(unbooked == 1 ? "It" : "They")} must be used by {deadline} " +
                    $"({WorkingDays(s.WorkingDaysLeft)} still free).",
                BalanceStatus.DaysWillBeLost =>
                    $"You have {Days(unbooked)} not yet booked, but only {WorkingDaysLeft(s.WorkingDaysLeft)} {window}.",
                BalanceStatus.OverBooked =>
                    $"You have booked {Days(s.HolidayYear.ExceedsEarnableBy)} more than you can earn by 31 August.",
                BalanceStatus.Expired =>
                    $"Holiday year {s.HolidayYear.HolidayYear} expired on {deadline}: " +
                    Outcome(s.HolidayYear.AtDeadline, s.NextHolidayYear, past: true) + ".",
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
                        lines.Add($"If they are not booked, {outcome}.");
                    break;
                case BalanceStatus.DaysWillBeLost:
                    lines.Add($"{Days(s.DaysAtRisk)} can't fit even if you book every free working day: " +
                        $"{Outcome(s.AtRisk, next, past: false)}.");
                    break;
                case BalanceStatus.AllPlanned when s.HolidayYear.TotalDays > 0 && s.TransferredToNext > 0:
                    lines.Add($"{Days(s.TransferredToNext)} {(PastDeadline ? (s.TransferredToNext == 1 ? "was" : "were") : "will be")} transferred to {next}.");
                    break;
            }

            if (transferredIn > 0 && s.Status is BalanceStatus.DaysToPlan or BalanceStatus.DaysWillBeLost)
            {
                var source = s.HolidayYear.HolidayYear == _data.EffectiveFirstTrackedHolidayYear(Today)
                    ? "of opening balance"
                    : $"transferred from {s.HolidayYear.HolidayYear.Previous}";
                lines.Add($"{Days(transferredIn)} {source} " +
                    $"must be used in this holiday year, or {(transferredIn == 1 ? "it is" : "they are")} lost.");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>"5 days will be transferred to 2026/27, 2 days will be paid out and 3 days will be lost" (zero parts omitted).</summary>
    private static string Outcome(DeadlineOutcome o, HolidayYear next, bool past)
    {
        var parts = new List<string>();
        if (o.TransferredDays > 0)
            parts.Add($"{Days(o.TransferredDays)} {(past ? (o.TransferredDays == 1 ? "was" : "were") : "will be")} transferred to {next}");
        if (o.PaidOutDays > 0)
            parts.Add($"{Days(o.PaidOutDays)} {(past ? (o.PaidOutDays == 1 ? "was" : "were") : "will be")} paid out");
        if (o.LostDays > 0)
            parts.Add($"{Days(o.LostDays)} {(past ? (o.LostDays == 1 ? "was" : "were") : "will be")} lost");
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
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

    public string AddButtonText => _bookFeriefri ? "Add Feriefri" : "Add vacation";

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
            return Bookings.IsRangeWithinLimit(start, end) ? $"{PreviewDays(start, end)} working day(s)" : "More than one year";
        }
    }

    private string PreviewDays(DateOnly start, DateOnly end)
    {
        var days = CountWorkingDays(start, end);
        return IsHalfDay && days > 0 ? "0.5" : days.ToString();
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
            ErrorMessage = "Pick a start and end date.";
            return;
        }

        var start = DateOnly.FromDateTime(startDate);
        var end = DateOnly.FromDateTime(endDate);
        if (end < start)
        {
            ErrorMessage = "The end date must be on or after the start date.";
            return;
        }
        if (!Bookings.IsRangeWithinLimit(start, end))
        {
            ErrorMessage = $"A booking can cover at most one year. The latest end date for a booking starting " +
                $"{DateText.Long(start)} is {DateText.Long(Bookings.LatestAllowedEnd(start))}.";
            return;
        }
        if (CountWorkingDays(start, end) == 0)
        {
            ErrorMessage = start == end && _data.FindPublicHoliday(start) is { } holiday
                ? $"{DateText.WithWeekday(start)} is a public holiday{(holiday.Name is { } name ? $" ({name})" : "")}."
                : "That period has no working days in it.";
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

        if (!Apply(() => _data.Days = result.Days))
            return;

        IsHalfDay = false;
        NewStart = null;
        NewEnd = null;
        NewDescription = "";

        var messages = new List<string>();
        if (!result.IsComplete)
        {
            var head = $"{FeriefriDays(result.BookedDays)} booked; {Days(result.LeftOutDays)} left out";
            if (result.Limit is { PaidOutAtFirstYearEnd: > 0 } reserved)
            {
                var paid = reserved.PaidOutAtFirstYearEnd;
                var usable = reserved.AllowanceDays - paid;
                var yearEnd = DateText.Long(new DateOnly(reserved.HolidayYear.StartYear, 12, 31));
                messages.Add($"{head}: from January only {usable:0.##} of the {reserved.AllowanceDays:0.##} Feriefri days for " +
                    $"{reserved.HolidayYear} can be used, because {paid:0.##} {(paid == 1 ? "is" : "are")} paid out in January " +
                    $"{reserved.HolidayYear.StartYear + 1} unless used by {yearEnd}.");
            }
            else
            {
                var year = result.Limit is { } limit ? $" for {limit.HolidayYear}" : "";
                messages.Add($"{head} because the Feriefri days{year} are used up.");
            }
        }
        foreach (var date in result.HalfDayDates)
            messages.Add($"{DateText.Long(date)} booked as a half day.");
        InfoMessage = string.Join(" ", messages);
    }

    private static string RefusedMessage(FeriefriLimit? limit)
    {
        if (limit is null)
            return "No Feriefri days could be booked.";

        var paidOut = limit.PaidOutAtFirstYearEnd > 0
            ? $" ({limit.PaidOutAtFirstYearEnd:0.##} are paid out in January {limit.HolidayYear.StartYear + 1})"
            : "";
        return $"All {limit.AllowanceDays:0.##} Feriefri days for {limit.HolidayYear} are used{paidOut}.";
    }

    private static string FeriefriDays(decimal days) => days == 1 ? "1 Feriefri day" : $"{days:0.##} Feriefri days";

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
            ErrorMessage = "Pick a date for the holiday.";
            return;
        }

        var date = DateOnly.FromDateTime(value);
        if (DanishHolidays.NameOf(date) is { } existing)
        {
            // Already a public holiday: nothing to store.
            NewHoliday = null;
            InfoMessage = $"{DateText.Long(date)} is already a public holiday ({existing}).";
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
            InfoMessage = $"Added to {date.Year}'s holidays.";
    }

    /// <summary>Deletes the stored records on public holidays of the selected year; they were never counted, so balances stay.</summary>
    private void RemoveConflictDays()
    {
        var dates = _data.BookedDaysOnHolidays(_settings.Year).Select(d => d.Date).ToList();
        if (dates.Count == 0)
            return;

        if (Apply(() => _data.Days = Bookings.RemoveDays(_data.Days, dates)))
            InfoMessage = dates.Count == 1
                ? "Removed 1 booked day on a public holiday."
                : $"Removed {dates.Count} booked days on public holidays.";
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
            ErrorMessage = $"Could not save: {ex.Message.TrimEnd('.', ' ')}. Your change was not saved.";
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

    private static string Days(decimal days) => days == 1 ? "1 day" : $"{days:0.##} days";

    private static string WorkingDays(decimal days) => days == 1 ? "1 working day is" : $"{days:0.##} working days are";

    private static string WorkingDaysLeft(decimal days) => $"{WorkingDays(days)} left";
}
