using System.Collections.ObjectModel;
using System.IO;
using VacationCalculator.App.Services;
using VacationCalculator.Core;

namespace VacationCalculator.App.ViewModels;

/// <summary>A booked period as shown for the selected year.</summary>
public sealed record EntryRow(BookedPeriod Period, bool IsPast);

/// <summary>One line of the holiday-years card.</summary>
public sealed record HolidayYearRow(
    string Name, string Earned, decimal TransferredIn, decimal Used, decimal BookedAhead,
    decimal Remaining, int RemainingSign, DateOnly Deadline, string Note, bool NoteIsError)
{
    public static HolidayYearRow From(HolidayYearBalance b)
    {
        var note = b.ExceedsEarnableBy > 0
            ? $"{b.ExceedsEarnableBy:0.##} more than can be earned by 31 August"
            : b.ExceedsEarnedSoFarBy > 0 ? $"advance holiday: {b.ExceedsEarnedSoFarBy:0.##} not earned yet" : "";
        return new HolidayYearRow(
            b.HolidayYear.ToString(), $"{b.EarnedSoFar:0.##} of {b.EarnableDays:0.##}", b.TransferredIn, b.UsedDays,
            b.BookedAheadDays, b.RemainingDays, Math.Sign(b.RemainingDays), b.Deadline, note, b.ExceedsEarnableBy > 0);
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

    public bool FifthWeekTransferAllowed
    {
        get => _data.FifthWeekTransferAllowed;
        set
        {
            if (value == _data.FifthWeekTransferAllowed)
                return;

            _data.FifthWeekTransferAllowed = value;
            OnPropertyChanged();
            SaveAndRefresh();
        }
    }

    /// <summary>Days transferred from the holiday year expiring on 31 December of the selected year (as applied by Core).</summary>
    public decimal TransferredToNext
    {
        get => Status.HolidayYear.TransferredToNext;
        set
        {
            var year = Status.HolidayYear;
            var clamped = Math.Clamp(value, 0, Math.Max(0, year.MaxTransferToNext));
            var stored = _data.FindHolidayYear(year.HolidayYear.StartYear)?.TransferredToNext ?? 0;
            if (clamped == stored)
            {
                OnPropertyChanged();
                return;
            }

            _data.GetOrAddHolidayYear(year.HolidayYear.StartYear).TransferredToNext = clamped;
            SaveAndRefresh();
        }
    }

    public string MaxTransferText => $"max {Status.HolidayYear.MaxTransferToNext:0.##}";

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
                OnPropertyChanged(nameof(TransferredToNext));
                OnPropertyChanged(nameof(MaxTransferText));
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
                BalanceStatus.AllPlanned =>
                    "Everything is used, booked or transferred. Nothing will expire.",
                BalanceStatus.DaysToPlan =>
                    $"You have {Days(unbooked)} not yet booked. {(unbooked == 1 ? "It" : "They")} must be used by {deadline} " +
                    $"({WorkingDays(s.WorkingDaysLeft)} still free).",
                BalanceStatus.DaysWillBeLost =>
                    $"You have {Days(unbooked)} not yet booked, but only {WorkingDaysLeft(s.WorkingDaysLeft)} {window}.",
                BalanceStatus.OverBooked =>
                    $"You have booked {Days(s.HolidayYear.ExceedsEarnableBy)} more than you can earn by 31 August.",
                BalanceStatus.Expired =>
                    $"Holiday year {s.HolidayYear.HolidayYear} expired on {deadline}: " +
                    JoinAnd(
                        s.PaidOutDays > 0 ? $"{Days(s.PaidOutDays)} {(s.PaidOutDays == 1 ? "was" : "were")} paid out" : null,
                        s.LostDays > 0 ? $"{Days(s.LostDays)} {(s.LostDays == 1 ? "was" : "were")} lost" : null) + ".",
                _ => "",
            };
        }
    }

    /// <summary>Second, smaller line of the banner: what happens to the unbooked days, and the 5th-week hint.</summary>
    public string StatusDetail
    {
        get
        {
            var s = Status;
            var lines = new List<string>();
            var unusedFifth = s.HolidayYear.UnusedFifthWeekDays;
            // When a transfer is still possible, the 5th-week sentence below carries the "otherwise paid out" fact.
            var transferHint = s.Status != BalanceStatus.Expired && unusedFifth > 0
                && s.TransferOptionApplies && s.HolidayYear.TransferredToNext == 0;
            switch (s.Status)
            {
                case BalanceStatus.DaysToPlan:
                    var outcome = JoinAnd(
                        !transferHint && s.PaidOutDays > 0 ? $"{Days(s.PaidOutDays)} will be paid out" : null,
                        s.LostDays > 0 ? $"{Days(s.LostDays)} will be lost" : null);
                    if (outcome.Length > 0)
                        lines.Add($"If they are not booked, {outcome}.");
                    break;
                case BalanceStatus.DaysWillBeLost:
                    if (transferHint)
                    {
                        if (s.AtRiskLostDays > 0)
                            lines.Add($"{Days(s.AtRiskLostDays)} will be lost even if you book every free working day.");
                        break;
                    }
                    var atRisk = JoinAnd(
                        s.AtRiskLostDays > 0 ? $"{Days(s.AtRiskLostDays)} will be lost" : null,
                        s.AtRiskPaidOutDays > 0 ? $"{Days(s.AtRiskPaidOutDays)} will be paid out" : null);
                    lines.Add($"{Days(s.DaysAtRisk)} can't fit even if you book every free working day: {atRisk}.");
                    break;
            }

            if (transferHint)
                lines.Add($"The unused 5th-week days ({Days(unusedFifth)}) can be transferred by agreement before 31 December, otherwise they are paid out.");
            return string.Join(Environment.NewLine, lines);
        }
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
        OnPropertyChanged(nameof(NewEntryPreview));
    }

    private static string JoinAnd(params string?[] parts) => string.Join(" and ", parts.Where(p => p is not null));

    private static string Days(decimal days) => days == 1 ? "1 day" : $"{days:0.##} days";

    private static string WorkingDays(decimal days) => days == 1 ? "1 working day is" : $"{days:0.##} working days are";

    private static string WorkingDaysLeft(decimal days) => $"{WorkingDays(days)} left";
}
