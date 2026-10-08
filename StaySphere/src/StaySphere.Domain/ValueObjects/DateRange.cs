using StaySphere.Domain.Common;

namespace StaySphere.Domain.ValueObjects;

/// <summary>A stay: check-in date (inclusive) to check-out date (exclusive). Each date in between is one night.</summary>
public readonly record struct DateRange
{
    public const int MaxNights = 90;

    private DateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }

    public int Nights => End.DayNumber - Start.DayNumber;

    public static Result<DateRange> Create(DateOnly checkIn, DateOnly checkOut)
    {
        if (checkOut <= checkIn)
            return Error.Validation("dates.invalid", "Check-out must be after check-in.");
        if (checkOut.DayNumber - checkIn.DayNumber > MaxNights)
            return Error.Validation("dates.too_long", $"Stays are limited to {MaxNights} nights.");
        return new DateRange(checkIn, checkOut);
    }

    public IEnumerable<DateOnly> EachNight()
    {
        for (var d = Start; d < End; d = d.AddDays(1))
            yield return d;
    }

    public bool Overlaps(DateRange other) => Start < other.End && other.Start < End;

    public bool Contains(DateOnly night) => night >= Start && night < End;
}
