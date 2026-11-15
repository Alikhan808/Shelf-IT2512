using System.Collections.Immutable;

namespace Shelf.Core;

public sealed partial class Library
{
    public ImmutableArray<Title> Titles { get; }
    public ImmutableArray<Member> Members { get; }
    public ImmutableArray<Loan> Loans { get; }
    public ImmutableArray<Hold> Holds { get; }

    private Library(ImmutableArray<Title> titles, ImmutableArray<Member> members,
        ImmutableArray<Loan> loans, ImmutableArray<Hold> holds) =>
        (Titles, Members, Loans, Holds) = (titles, members, loans, holds);

    public static Result<Library> Create(IEnumerable<Title> titles, IEnumerable<Member> members,
        IEnumerable<Loan> loans, IEnumerable<Hold> holds)
    {
        var state = new Library(titles.ToImmutableArray(), members.ToImmutableArray(),
            loans.ToImmutableArray(), holds.ToImmutableArray());

        if (state.Titles.Any(title => title is null) || state.Members.Any(member => member is null) ||
            state.Loans.Any(loan => loan is null) || state.Holds.Any(hold => hold is null))
            return new Result<Library>.Fail("invalid-state", "Library collections may not contain null entries.");

        var titleIds = state.Titles.Select(title => title.Id).ToHashSet();
        var memberIds = state.Members.Select(member => member.Id).ToHashSet();
        if (titleIds.Count != state.Titles.Length || memberIds.Count != state.Members.Length)
            return new Result<Library>.Fail("duplicate-id", "Title and member IDs must be unique within their collections.");

        var loanKeys = state.Loans.Select(loan => (loan.TitleId, loan.MemberId)).ToHashSet();
        var holdKeys = state.Holds.Select(hold => (hold.TitleId, hold.MemberId)).ToHashSet();
        if (loanKeys.Count != state.Loans.Length || holdKeys.Count != state.Holds.Length || loanKeys.Overlaps(holdKeys))
            return new Result<Library>.Fail("duplicate-activity", "A member may hold or borrow a title only once.");

        if (loanKeys.Concat(holdKeys).Any(key => !titleIds.Contains(key.TitleId) || !memberIds.Contains(key.MemberId)))
            return new Result<Library>.Fail("unknown-reference", "All loans and holds must reference existing titles and members.");

        var checkedOut = state.Loans.GroupBy(loan => loan.TitleId).ToDictionary(group => group.Key, group => group.Count());
        if (state.Titles.Any(title => checkedOut.GetValueOrDefault(title.Id) > title.Copies))
            return new Result<Library>.Fail("overallocated", "Active loans exceed the number of copies.");

        return new Result<Library>.Ok(state);
    }

    public Lookup<Title> FindTitle(string id) =>
        Titles.FirstOrDefault(title => title.Id == id) is { } title
            ? new Lookup<Title>.Found(title)
            : new Lookup<Title>.Missing(id);

    public Lookup<Member> FindMember(string id) =>
        Members.FirstOrDefault(member => member.Id == id) is { } member
            ? new Lookup<Member>.Found(member)
            : new Lookup<Member>.Missing(id);

    public Lookup<Loan> FindLoan(string titleId, string memberId) =>
        Loans.FirstOrDefault(loan => loan.TitleId == titleId && loan.MemberId == memberId) is { } loan
            ? new Lookup<Loan>.Found(loan)
            : new Lookup<Loan>.Missing($"{titleId}/{memberId}");

    private int FreeCopies(Title title) => title.Copies - Loans.Count(loan => loan.TitleId == title.Id);

    private int QueueLength(string titleId) => Holds.Count(hold => hold.TitleId == titleId);
}
