using System.Globalization;
using VacationCalculator.App.Services;
using VacationCalculator.Core;

namespace VacationCalculator.App.ViewModels;

/// <summary>How a day cell is filled. Only a counted booking on a working day shows as Vacation or Feriefri.</summary>
public enum DayFill
{
    Plain,
    Weekend,
    Holiday,
    Vacation,
    Feriefri,
}

/// <summary>The temporary highlight of a day cell while a range is dragged.</summary>
public enum DragKind
{
    None,
    Vacation,
    Feriefri,
}

/// <summary>The ISO week number at the start of a calendar row.</summary>
public sealed class WeekNumberCell : ObservableObject
{
    private string _text = "";

    public WeekNumberCell(int weekNumber) => Update(weekNumber);

    public string Text
    {
        get => _text;
        private set => SetField(ref _text, value);
    }

    public void Update(int weekNumber) => Text = weekNumber.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One day of a month card. Only presents the Core <see cref="CalendarDay"/>: no day logic here. Cells are updated in
/// place when the year changes (<see cref="Update"/>), so the UI keeps its elements and only re-reads changed values.
/// </summary>
public sealed class DayCell : ObservableObject
{
    private DateOnly _date;
    private string _text = "";
    private DayFill _fill;
    private bool _isHalf;
    private bool _isToday;
    private bool _isPast;
    private bool _isConflict;
    private bool _isBlank;
    private string _tooltip = "";
    private bool _isBookable;
    private DragKind _drag;

    public DayCell(CalendarDay day) => Update(day);

    public DateOnly Date { get => _date; private set => SetField(ref _date, value); }
    public string Text { get => _text; private set => SetField(ref _text, value); }
    public DayFill Fill { get => _fill; private set => SetField(ref _fill, value); }
    public bool IsHalf { get => _isHalf; private set => SetField(ref _isHalf, value); }
    public bool IsToday { get => _isToday; private set => SetField(ref _isToday, value); }
    public bool IsPast { get => _isPast; private set => SetField(ref _isPast, value); }
    public bool IsConflict { get => _isConflict; private set => SetField(ref _isConflict, value); }

    /// <summary>A day of the neighbouring month that only pads the week; drawn invisible.</summary>
    public bool IsBlank { get => _isBlank; private set => SetField(ref _isBlank, value); }

    public string Tooltip { get => _tooltip; private set => SetField(ref _tooltip, value); }

    /// <summary>A working day of the month: clicking it books or cycles it (hand cursor).</summary>
    public bool IsBookable { get => _isBookable; private set => SetField(ref _isBookable, value); }

    /// <summary>Highlight while a range is being dragged over this cell; the kind follows the mouse button.</summary>
    public DragKind Drag { get => _drag; set => SetField(ref _drag, value); }

    public void Update(CalendarDay day)
    {
        Date = day.Date;
        IsBlank = !day.IsInMonth;
        IsBookable = day.IsInMonth && day.IsWorkingDay;
        Text = day.Date.Day.ToString(CultureInfo.InvariantCulture);
        if (IsBlank)
        {
            IsToday = IsPast = IsConflict = IsHalf = false;
            Fill = DayFill.Plain;
            Tooltip = "";
            return;
        }

        IsToday = day.IsToday;
        IsPast = day.IsPast;
        IsConflict = day.IsConflict;

        var counted = day.IsBooked && day.IsWorkingDay && day.BookedAmount > 0;
        IsHalf = counted && day.IsHalfDay;
        Fill = counted ? (day.BookedType == DayType.Feriefri ? DayFill.Feriefri : DayFill.Vacation)
            : day.IsPublicHoliday ? DayFill.Holiday
            : day.IsWeekend ? DayFill.Weekend
            : DayFill.Plain;
        Tooltip = BuildTooltip(day);
    }

    private static string BuildTooltip(CalendarDay day)
    {
        var lines = new List<string> { DateText.WithWeekday(day.Date) };

        if (day.IsDanishHoliday)
            lines.Add(day.HolidayKind is { } kind ? Loc.HolidayName(kind) : Loc.Get("Legend.PublicHoliday"));
        else if (day.IsManualHoliday)
            lines.Add(Loc.Get("Cal.PublicHolidayManual"));
        else if (day.IsWeekend)
            lines.Add(Loc.Get("Legend.Weekend"));

        if (day.IsBooked && (day.IsConflict || day.BookedAmount > 0))
        {
            var type = Loc.Get(day.BookedType == DayType.Feriefri ? "Type.Feriefri" : "Type.Vacation");
            lines.Add(Loc.Format("Cal.Booked", type, Loc.Get(day.IsHalfDay ? "Cal.HalfDay" : "Cal.FullDay")));
            if (day.Description.Length > 0)
                lines.Add(Loc.Format("Cal.Note", day.Description));
            if (day.IsConflict)
                lines.Add(Loc.Get("Cal.Conflict"));
            else if (day.Allocations.Count > 1)
                lines.Add(string.Join(", ", day.Allocations.Select(a => Loc.Format("Cal.Allocation", Loc.Num(a.Amount), a.HolidayYear.ToString()))));
            else if (day.HolidayYearsText.Length > 0)
                lines.Add(Loc.Format("Cal.HolidayYear", day.HolidayYearsText));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// A month card: its name and a flat row-major list of <see cref="WeekNumberCell"/> and <see cref="DayCell"/>, 8 per week.
/// <see cref="Update"/> reuses the cell objects when the month has the same number of weeks; otherwise it replaces <see cref="Cells"/>.
/// </summary>
public sealed class MonthView : ObservableObject
{
    private string _name = "";
    private IReadOnlyList<object> _cells = [];

    public MonthView(CalendarMonth month) => Update(month);

    public string Name { get => _name; private set => SetField(ref _name, value); }

    public IReadOnlyList<object> Cells { get => _cells; private set => SetField(ref _cells, value); }

    public void Update(CalendarMonth month)
    {
        Name = Loc.MonthTitle(month.Month);
        if (_cells.Count == month.Weeks.Count * 8)
        {
            var i = 0;
            foreach (var week in month.Weeks)
            {
                ((WeekNumberCell)_cells[i++]).Update(week.WeekNumber);
                foreach (var day in week.Days)
                    ((DayCell)_cells[i++]).Update(day);
            }
            return;
        }

        var cells = new List<object>(month.Weeks.Count * 8);
        foreach (var week in month.Weeks)
        {
            cells.Add(new WeekNumberCell(week.WeekNumber));
            foreach (var day in week.Days)
                cells.Add(new DayCell(day));
        }
        Cells = cells;
    }
}
