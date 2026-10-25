using VacationCalculator.Core;

namespace VacationCalculator.App.ViewModels;

/// <summary>
/// A mouse gesture on the calendar: pressed on <paramref name="Anchor"/>, released (or, for a preview, currently) on
/// <paramref name="Date"/>. <paramref name="Type"/> follows the button (left Vacation, right Feriefri).
/// </summary>
public sealed record CalendarGesture(DateOnly Anchor, DateOnly Date, DayType Type, bool Shift);
