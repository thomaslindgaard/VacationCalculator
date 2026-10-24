using System.Collections.ObjectModel;
using System.IO;
using VacationCalculator.App.Services;
using VacationCalculator.Core;

namespace VacationCalculator.App.ViewModels;

/// <summary>A booked period as shown for the selected year.</summary>
public sealed record EntryRow(BookedPeriod Period, bool IsPast);

public sealed class MainViewModel : ObservableObject
{
    private const decimal DefaultAllowance = 25;

    private readonly DataStore _store;
    private readonly VacationData _data;
    private YearSettings _settings;
    private YearBalance _balance;

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
        _balance = BalanceCalculator.Calculate(_settings, _data.Days, Today);

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
            OnPropertyChanged(nameof(AllowanceDays));
            OnPropertyChanged(nameof(CarryOverDays));
            Refresh();
        }
    }

    public decimal AllowanceDays
    {
        get => _settings.AllowanceDays;
        set
        {
            if (value == _settings.AllowanceDays)
                return;

            _settings.AllowanceDays = Math.Max(0, value);
            OnPropertyChanged();
            SaveAndRefresh();
        }
    }

    public decimal CarryOverDays
    {
        get => _settings.CarryOverDays;
        set
        {
            if (value == _settings.CarryOverDays)
                return;

            _settings.CarryOverDays = Math.Max(0, value);
            OnPropertyChanged();
            SaveAndRefresh();
        }
    }

    public YearBalance Balance
    {
        get => _balance;
        private set
        {
            if (SetField(ref _balance, value))
            {
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(RemainingSign));
            }
        }
    }

    /// <summary>Sign (-1, 0, 1) of the not-yet-booked days; the "Not yet booked" figure is coloured by it.</summary>
    public int RemainingSign => Math.Sign(Balance.RemainingDays);

    public string StatusMessage
    {
        get
        {
            var b = Balance;
            var deadline = $"31 December {b.Year}";
            // The free-day count starts today in the current year, and covers the whole year for a future one.
            var window = b.Year > Today.Year
                ? $"in {b.Year} up to and including {deadline}"
                : $"from today through {deadline}";
            return b.Status switch
            {
                BalanceStatus.AllPlanned =>
                    (b.TotalDays == 1
                        ? $"The {Days(b.TotalDays)} is used or booked."
                        : $"All {Days(b.TotalDays)} are used or booked.") + " Nothing will expire.",
                BalanceStatus.DaysToPlan =>
                    $"You have {Days(b.RemainingDays)} not yet booked. {(b.RemainingDays == 1 ? "It" : "They")} must be used by {deadline} " +
                    $"({WorkingDays(b.WorkingDaysLeft)} still free).",
                BalanceStatus.DaysWillBeLost =>
                    $"You have {Days(b.RemainingDays)} not yet booked, but only {WorkingDaysLeft(b.WorkingDaysLeft)} " +
                    $"{window}. {Days(b.DaysAtRisk)} will be lost even if you book every remaining day.",
                BalanceStatus.OverBooked =>
                    $"You have booked {Days(-b.RemainingDays)} more than your allowance for {b.Year}.",
                BalanceStatus.Expired =>
                    $"{Days(b.RemainingDays)} {(b.RemainingDays == 1 ? "was" : "were")} not used in {b.Year} and {(b.RemainingDays == 1 ? "has" : "have")} expired.",
                _ => "",
            };
        }
    }

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

        // Start a new year with the allowance of the nearest earlier year, if there is one.
        var previous = _data.Years.Where(y => y.Year < year).MaxBy(y => y.Year);
        var settings = new YearSettings { Year = year, AllowanceDays = previous?.AllowanceDays ?? DefaultAllowance };
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

        Balance = BalanceCalculator.Calculate(_settings, _data.Days, Today);
        OnPropertyChanged(nameof(NewEntryPreview));
    }

    private static string Days(decimal days) => days == 1 ? "1 day" : $"{days:0.##} days";

    private static string WorkingDays(decimal days) => days == 1 ? "1 working day is" : $"{days:0.##} working days are";

    private static string WorkingDaysLeft(decimal days) => $"{WorkingDays(days)} left";
}
