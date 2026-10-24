using System.Globalization;
using VacationCalculator.App.Services;
using VacationCalculator.App.ViewModels;
using VacationCalculator.Core;

namespace VacationCalculator.App.Tests;

/// <summary>Dates in texts are English whatever the culture; numbers still follow the culture.</summary>
public class MainViewModelDateTextCultureTests : HolidayYearVmTestBase
{
    [Theory]
    [InlineData("da-DK", "2,5")]
    [InlineData("en-US", "2.5")]
    [InlineData("de-DE", "2,5")]
    public void StatusMessage_DateEnglish_NumberFollowsCulture(string culture, string number)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        // 22.5 used of 25 -> 2.5 unbooked; Fri 2026-12-18 has 7 free working days (24, 25, 31 Dec are holidays).
        var vm = NewVm(Dec18, Seed(22.5m));
        Assert.Equal(
            $"You have {number} days not yet booked. They must be used by 31 December 2026 (7 working days are still free).",
            vm.StatusMessage);
    }

    [Theory]
    [InlineData("da-DK")]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void Titles_Tooltip_AndError_AreEnglish(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var vm = NewVm(Dec18, Seed(20));
        Assert.Equal("Holiday year 2025/26 · use by 31 December 2026", vm.HolidayYearTitle);
        Assert.Contains("(31 December 2026)", vm.DeadlineSettingsTitle);

        // Start 2026-03-15 (Sun): latest end 2027-03-14.
        vm.NewStart = new DateTime(2026, 3, 15);
        vm.NewEnd = new DateTime(2027, 3, 15);
        vm.AddEntryCommand.Execute(null);
        Assert.Equal("A booking can cover at most one year. The latest end date for a booking starting 15 March 2026 is 14 March 2027.",
            vm.ErrorMessage);
    }

    [Theory]
    [InlineData("da-DK")]
    [InlineData("en-US")]
    public void DateText_Formats(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        // Thu 2026-12-24; Mon 2028-02-29 (leap day); Sat 2026-01-03 (single-digit day not padded).
        Assert.Equal("Thursday 24 December 2026", DateText.WithWeekday(new DateOnly(2026, 12, 24)));
        Assert.Equal("29 February 2028", DateText.Long(new DateOnly(2028, 2, 29)));
        Assert.Equal("3 January 2026", DateText.Long(new DateOnly(2026, 1, 3)));
        Assert.Equal("Saturday 3 January 2026", DateText.WithWeekday(new DateOnly(2026, 1, 3)));
        var c = new DateTextConverter();
        Assert.Equal("Thursday 24 December 2026",
            c.Convert(new DateOnly(2026, 12, 24), typeof(string), "Weekday", CultureInfo.CurrentCulture));
        Assert.Equal("24 December 2026",
            c.Convert(new DateOnly(2026, 12, 24), typeof(string), null!, CultureInfo.CurrentCulture));
    }
}
