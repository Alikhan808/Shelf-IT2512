namespace Shelf.Core;

public sealed record OverdueRow(string TitleId, string MemberId, DateOnly DueOn, int DaysLate);
public sealed record SectionRow(string Section, long Copies, long CheckedOut, long WalkInCopies, long Waiting);

public static class Reports
{
    // The threshold stays fixed while the date and loan snapshot can vary.
    public static Func<DateOnly, Func<IEnumerable<Loan>, IEnumerable<OverdueRow>>> OverdueFrom(int minimumDays) =>
        date => loans => StreamOverdue(loans, date, minimumDays);

    public static Func<DateOnly, Func<IEnumerable<Loan>, IEnumerable<OverdueRow>>> AnyOverdue { get; } = OverdueFrom(1);
    public static Func<DateOnly, Func<IEnumerable<Loan>, IEnumerable<OverdueRow>>> WeekOverdue { get; } = OverdueFrom(7);

    private static IEnumerable<OverdueRow> StreamOverdue(IEnumerable<Loan> loans, DateOnly date, int minimumDays)
    {
        foreach (var loan in loans)
        {
            int days = Library.DaysLate(loan, date);
            if (days >= minimumDays)
                yield return new OverdueRow(loan.TitleId, loan.MemberId, loan.DueOn, days);
        }
    }

    public static OverdueRow[] Overdue(Library state, DateOnly date, bool weekOnly)
    {
        var configured = weekOnly ? WeekOverdue : AnyOverdue;
        return configured(date)(state.Loans)
            .OrderByDescending(row => row.DaysLate)
            .ThenBy(row => row.TitleId, StringComparer.Ordinal)
            .ThenBy(row => row.MemberId, StringComparer.Ordinal)
            .ToArray();
    }

    public static SectionRow[] Availability(Library state)
    {
        var loans = state.Loans.ToLookup(loan => loan.TitleId);
        var holds = state.Holds.ToLookup(hold => hold.TitleId);
        return state.Titles
            .Select(title => new
            {
                title.Section,
                title.Copies,
                CheckedOut = loans[title.Id].Count(),
                Queued = holds[title.Id].Count()
            })
            .GroupBy(title => title.Section, StringComparer.Ordinal)
            .Select(group => new SectionRow(group.Key,
                group.Sum(title => (long)title.Copies),
                group.Sum(title => (long)title.CheckedOut),
                group.Sum(title => (long)Math.Max(0, title.Copies - title.CheckedOut - title.Queued)),
                group.Sum(title => (long)title.Queued)))
            .OrderBy(row => row.WalkInCopies)
            .ThenBy(row => row.Section, StringComparer.Ordinal)
            .ToArray();
    }
}
