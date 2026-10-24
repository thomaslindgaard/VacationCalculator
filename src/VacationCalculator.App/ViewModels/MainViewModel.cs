using System.Collections.ObjectModel;
using System.IO;
using VacationCalculator.App.Services;
using VacationCalculator.Core;

namespace VacationCalculator.App.ViewModels;

/// <summary>A booked period as shown for the selected year.</summary>
public sealed record EntryRow(BookedPeriod Period, bool IsPast);

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
            $"Projected transfer from {b.HolidayYear.Previous} on {b.HolidayYear.Previous.Deadline:D}. " +
            $"Until then these days still count as remaining in {b.HolidayYear.Previous}.");
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly DataStore _store;
    private readonly VacationData _data;
    private YearSettings _settings;
    private CalendarYearStatus _status;
    private IReadOnlyList<HolidayYearRow> _holidayYearRows = [];

    private DateTime? _newStart;
    private DateTime? _newEnd;
    private string _newDescription = "";
    private bool _isHalfDay;
    private DateTime? _newHoliday;
    private string _errorMessage = "";

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
        _settings = GetOrCreateSettings(Today.Year);
        _status = BalanceCalculator.CalculateCalendarYear(_data, _settings.Year, Today);

        PreviousYearCommand = new RelayCommand(_ => SelectedYear--);
        NextYearCommand = new RelayCommand(_ => SelectedYear++);
        CurrentYearCommand = new RelayCommand(_ => SelectedYear = Today.Year);
        AddEntryCommand = new RelayCommand(_ => AddEntry());
        DeleteEntryCommand = new RelayCommand(p => DeleteEntry((EntryRow)p!));
        AddHolidayCommand = new RelayCommand(_ => AddHoliday());
        RemoveHolidayCommand = new RelayCommand(p => RemoveHoliday((DateOnly)p!));
        ResetTransferLimitCommand = new RelayCommand(_ => { _data.ClearTransferLimit(DeadlineYear); SaveAndRefresh(); });
        ResetPayoutCommand = new RelayCommand(_ => { _data.ClearPayout(DeadlineYear); SaveAndRefresh(); });

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
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand DeleteEntryCommand { get; }
    public RelayCommand AddHolidayCommand { get; }
    public RelayCommand RemoveHolidayCommand { get; }
    public RelayCommand ResetTransferLimitCommand { get; }
    public RelayCommand ResetPayoutCommand { get; }

    public ObservableCollection<EntryRow> Entries { get; } = [];
    public ObservableCollection<DateOnly> Holidays { get; } = [];

    public int SelectedYear
    {
        get => _settings.Year;
        set
        {
            if (value == _settings.Year)
                return;

            _settings = GetOrCreateSettings(value);
            ErrorMessage = "";
            OnPropertyChanged();
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

            _data.EmployedSince = date;
            OnPropertyChanged();
            SaveAndRefresh();
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

            _data.FirstTrackedHolidayYear = value.StartYear;
            SaveAndRefresh();
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

            _data.OpeningBalanceDays = value;
            SaveAndRefresh();
        }
    }

    /// <summary>Employment-wide: whether unused days are transferred to the next holiday year at each deadline.</summary>
    public bool TransferAllowed
    {
        get => _data.TransferAllowed;
        set
        {
            if (value == _data.TransferAllowed)
                return;

            _data.TransferAllowed = value;
            OnPropertyChanged();
            SaveAndRefresh();
        }
    }

    private HolidayYear DeadlineYear => Status.HolidayYear.HolidayYear;
    private DeadlineSettings DeadlineSettings => Status.HolidayYear.Settings;

    /// <summary>Heading of the per-holiday-year settings in the sidebar.</summary>
    public string DeadlineSettingsTitle => $"When {DeadlineYear} expires ({DeadlineYear.Deadline:D})";

    public decimal TransferLimitDays
    {
        get => DeadlineSettings.TransferLimitDays;
        set
        {
            if (DeadlineSettings.TransferLimitIsOwn && value == DeadlineSettings.TransferLimitDays)
                return;

            _data.SetTransferLimit(DeadlineYear, value);
            SaveAndRefresh();
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

            _data.SetPayout(DeadlineYear, PayoutMode.Some, value);
            SaveAndRefresh();
        }
    }

    public string PayoutSource => Source(DeadlineSettings.PayoutFrom, DeadlineSettings.PayoutIsOwn);
    public bool PayoutIsOwn => DeadlineSettings.PayoutIsOwn;

    private void SetPayoutMode(PayoutMode mode)
    {
        if (mode == DeadlineSettings.PayoutMode)
            return;

        _data.SetPayout(DeadlineYear, mode, DeadlineSettings.PayoutDays);
        SaveAndRefresh();
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
    /// <summary>The holiday years open around the selected calendar year (the expiring one and the next).</summary>
    public IReadOnlyList<HolidayYearRow> HolidayYearRows
    {
        get => _holidayYearRows;
        private set => SetField(ref _holidayYearRows, value);
    }

    public string HolidayYearTitle =>
        $"Holiday year {Status.HolidayYear.HolidayYear} · use by 31 December {Status.Year}";

    public string EarnableNote => $"of {Status.HolidayYear.EarnableDays:0.##} earnable";

    /// <summary>Sign (-1, 0, 1) of the not-yet-booked days; the "Not yet booked" figure is coloured by it.</summary>
    public int UnbookedSign => Math.Sign(Status.UnbookedDays);

    public string StatusMessage
    {
        get
        {
            var s = Status;
            var deadline = $"31 December {s.Year}";
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

    public string NewDescription
    {
        get => _newDescription;
        set => SetField(ref _newDescription, value);
    }

    public string NewEntryPreview =>
        NewStart is { } start && NewEnd is { } end && end >= start
            ? $"{PreviewDays(DateOnly.FromDateTime(start), DateOnly.FromDateTime(end))} working day(s)"
            : "";

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

    private void AddEntry()
    {
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
        if (CountWorkingDays(start, end) == 0)
        {
            ErrorMessage = "That period has no working days in it.";
            return;
        }

        var holidays = _data.AllPublicHolidays();
        _data.Days = IsHalfDay && start == end
            ? Bookings.BookHalfDay(_data.Days, start, DayType.Vacation, NewDescription.Trim(), holidays)
            : Bookings.BookRange(_data.Days, start, end, DayType.Vacation, NewDescription.Trim(), holidays);
        IsHalfDay = false;
        NewStart = null;
        NewEnd = null;
        NewDescription = "";
        ErrorMessage = "";
        SaveAndRefresh();
    }

    private void DeleteEntry(EntryRow row)
    {
        _data.Days = Bookings.RemoveDays(_data.Days, row.Period.Dates);
        SaveAndRefresh();
    }

    private void AddHoliday()
    {
        if (NewHoliday is not { } value)
        {
            ErrorMessage = "Pick a date for the holiday.";
            return;
        }

        var date = DateOnly.FromDateTime(value);
        var settings = GetOrCreateSettings(date.Year);
        if (!settings.PublicHolidays.Contains(date))
        {
            settings.PublicHolidays.Add(date);
            settings.PublicHolidays.Sort();
        }

        NewHoliday = null;
        ErrorMessage = date.Year == SelectedYear ? "" : $"Added to {date.Year}'s holidays.";
        SaveAndRefresh();
    }

    private void RemoveHoliday(DateOnly date)
    {
        _settings.PublicHolidays.Remove(date);
        SaveAndRefresh();
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

    private void SaveAndRefresh()
    {
        try
        {
            _data.EnsureFirstTrackedHolidayYear(Today);
            _store.Save(_data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not save to {_store.Path}: {ex.Message}";
        }
        Refresh();
    }

    private void Refresh()
    {
        Entries.Clear();
        foreach (var period in Bookings.GroupPeriods(_data.Days, _settings.Year, _data.AllPublicHolidays()))
            Entries.Add(new EntryRow(period, period.End <= Today));

        Holidays.Clear();
        foreach (var holiday in _settings.PublicHolidays.Order())
            Holidays.Add(holiday);

        Status = BalanceCalculator.CalculateCalendarYear(_data, _settings.Year, Today);
        HolidayYearRows = BalanceCalculator.CalculateHolidayYears(_data, HolidayYear.InCalendarYear(_settings.Year), Today)
            .Select(HolidayYearRow.From).ToList();
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
