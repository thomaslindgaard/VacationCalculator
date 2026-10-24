using VacationCalculator.Core;

namespace VacationCalculator.Core.Tests;

/// <summary>Independent checks of the one-year booking cap; expected dates hand-counted.</summary>
public class BookingsYearCapIndependentTests
{
    private static readonly HashSet<DateOnly> NoHolidays = new();

    public static IEnumerable<object[]> Limits => new[]
    {
        // 1 Jan 2026 .. 31 Dec 2026 is exactly one year.
        new object[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31) },
        new object[] { new DateOnly(2026, 3, 15), new DateOnly(2027, 3, 14) },
        // 31 Jan 2026 + 1 year = 31 Jan 2027; day before = 30 Jan 2027.
        new object[] { new DateOnly(2026, 1, 31), new DateOnly(2027, 1, 30) },
        // 29 Feb 2028 has no anniversary in 2029; treated as 1 Mar, so ends 28 Feb 2029.
        new object[] { new DateOnly(2028, 2, 29), new DateOnly(2029, 2, 28) },
        // 28 Feb 2028 + 1 year = 28 Feb 2029; day before = 27 Feb 2029.
        new object[] { new DateOnly(2028, 2, 28), new DateOnly(2029, 2, 27) },
        // 28 Feb 2027 -> 27 Feb 2028 (before the 2028 leap day).
        new object[] { new DateOnly(2027, 2, 28), new DateOnly(2028, 2, 27) },
        // 1 Mar 2027 -> 29 Feb 2028 (leap day is the last allowed day).
        new object[] { new DateOnly(2027, 3, 1), new DateOnly(2028, 2, 29) },
        new object[] { new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31) },
        new object[] { new DateOnly(2026, 12, 31), new DateOnly(2027, 12, 30) },
        new object[] { new DateOnly(9998, 12, 31), new DateOnly(9999, 12, 30) },
        new object[] { new DateOnly(9999, 1, 1), DateOnly.MaxValue },
        new object[] { new DateOnly(9999, 6, 15), DateOnly.MaxValue },
        new object[] { DateOnly.MinValue, new DateOnly(1, 12, 31) },
    };

    [Theory]
    [MemberData(nameof(Limits))]
    public void LatestAllowedEnd_IsHandCounted(DateOnly start, DateOnly expected) =>
        Assert.Equal(expected, Bookings.LatestAllowedEnd(start));

    [Theory]
    [MemberData(nameof(Limits))]
    public void LatestEndIsAllowed_AndNextDayIsNot_InEitherOrder(DateOnly start, DateOnly latest)
    {
        Assert.True(Bookings.IsRangeWithinLimit(start, latest));
        Assert.True(Bookings.IsRangeWithinLimit(latest, start));
        if (latest != DateOnly.MaxValue)
        {
            var tooLate = latest.AddDays(1);
            Assert.False(Bookings.IsRangeWithinLimit(start, tooLate));
            Assert.False(Bookings.IsRangeWithinLimit(tooLate, start));
        }
    }

    [Fact]
    public void FullRangeFromMinToMax_IsOverTheLimit() =>
        Assert.False(Bookings.IsRangeWithinLimit(DateOnly.MinValue, DateOnly.MaxValue));

    [Fact]
    public void SingleDay_AndSameDayReversed_AreWithinLimit()
    {
        Assert.True(Bookings.IsRangeWithinLimit(new DateOnly(2026, 5, 5), new DateOnly(2026, 5, 5)));
        Assert.True(Bookings.IsRangeWithinLimit(DateOnly.MaxValue, DateOnly.MaxValue));
        Assert.True(Bookings.IsRangeWithinLimit(DateOnly.MinValue, DateOnly.MinValue));
    }

    [Fact]
    public void BookRange_FullYear2026_Books261WeekdaysExactly()
    {
        // 2026-01-01 is a Thursday; 365 days = 52 weeks + 1 Thursday -> 52*5+1 = 261 weekdays.
        var result = Bookings.BookRange(new List<BookedDay>(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            DayType.Vacation, "", NoHolidays);
        Assert.Equal(261, result.Count);
    }

    [Fact]
    public void BookRange_OneDayTooLong_ThrowsAndLeavesInputUntouched()
    {
        var existing = new List<BookedDay>();
        Assert.Throws<ArgumentException>(() => Bookings.BookRange(existing, new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1), DayType.Vacation, "", NoHolidays));
        Assert.Empty(existing);
    }

    [Fact]
    public void BookRange_ReversedAndTooLong_Throws() =>
        Assert.Throws<ArgumentException>(() => Bookings.BookRange(new List<BookedDay>(), new DateOnly(2027, 1, 1),
            new DateOnly(2026, 1, 1), DayType.Vacation, "", NoHolidays));

    [Fact]
    public void BookRange_LeapStart_29Feb2028_To_28Feb2029_Ok_And_1Mar2029_Throws()
    {
        var ok = Bookings.BookRange(new List<BookedDay>(), new DateOnly(2028, 2, 29), new DateOnly(2029, 2, 28),
            DayType.Vacation, "", NoHolidays);
        Assert.NotEmpty(ok);
        Assert.Throws<ArgumentException>(() => Bookings.BookRange(new List<BookedDay>(), new DateOnly(2028, 2, 29),
            new DateOnly(2029, 3, 1), DayType.Vacation, "", NoHolidays));
    }

    [Fact]
    public void BookRange_Year9999_FullYear_Works()
    {
        // 9999-01-01 is a Friday; 365 days -> 52*5 + 1 = 261 weekdays.
        var r = Bookings.BookRange(new List<BookedDay>(), new DateOnly(9999, 1, 1), DateOnly.MaxValue,
            DayType.Vacation, "", NoHolidays);
        Assert.Equal(261, r.Count);
    }
}
