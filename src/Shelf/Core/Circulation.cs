namespace Shelf.Core;

public static class LoanPolicies
{
    public static int Campus(Member member) => member.Tier switch
    {
        Membership.Standard => 2,
        Membership.Extended => 4,
        _ => throw new InvalidOperationException("Unexpected membership.")
    };

    public static int ExamWeek(Member member) => member.Tier switch
    {
        Membership.Standard => 1,
        Membership.Extended => 2,
        _ => throw new InvalidOperationException("Unexpected membership.")
    };
}

public sealed record Change(Library State, string Receipt);

public sealed partial class Library
{
    private sealed record Borrower(Title Title, Member Member);
    private sealed record IssuePlan(Loan Loan, Hold? CollectedHold);
    public sealed record ReturnPlan(Library State, int DaysLate);

    public Result<Change> Checkout(string titleId, string memberId, DateOnly date, Func<Member, int> limit) =>
        Issue(titleId, memberId, date, limit, collect: false);

    public Result<Change> CollectHold(string titleId, string memberId, DateOnly date, Func<Member, int> limit) =>
        Issue(titleId, memberId, date, limit, collect: true);

    private Result<Change> Issue(string titleId, string memberId, DateOnly date, Func<Member, int> limit, bool collect) =>
        ResolveBorrower(titleId, memberId)
            .Bind(borrower => PlanIssue(borrower, date, limit, collect))
            .Map(CommitIssue);

    private Result<Borrower> ResolveBorrower(string titleId, string memberId) =>
        FindTitle(titleId).Require("unknown-title", "Title").Bind(title =>
            FindMember(memberId).Require("unknown-member", "Member").Map(member => new Borrower(title, member)));

    private Result<IssuePlan> PlanIssue(Borrower borrower, DateOnly date, Func<Member, int> limit, bool collect)
    {
        var (title, member) = borrower;
        if (FindLoan(title.Id, member.Id) is Lookup<Loan>.Found)
            return new Result<IssuePlan>.Fail("already-borrowed", "This member already has this title.");

        var firstHold = Holds.FirstOrDefault(hold => hold.TitleId == title.Id);
        if (collect && firstHold?.MemberId != member.Id)
            return new Result<IssuePlan>.Fail("not-first-in-queue", "Only the first member in the queue may collect.");
        if (!collect && Holds.Any(hold => hold.TitleId == title.Id && hold.MemberId == member.Id))
            return new Result<IssuePlan>.Fail("use-collect", "Use collect to claim your reservation.");
        if (FreeCopies(title) <= (collect ? 0 : QueueLength(title.Id)))
            return new Result<IssuePlan>.Fail("unavailable", "No copy is available for this operation.");

        int borrowingLimit = limit(member);
        if (Loans.Count(loan => loan.MemberId == member.Id) >= borrowingLimit)
            return new Result<IssuePlan>.Fail("loan-limit", $"The member has reached the policy limit of {borrowingLimit}.");

        return Loan.Create(title.Id, member.Id, date, null)
            .Map(loan => new IssuePlan(loan, collect ? firstHold : null));
    }

    private Change CommitIssue(IssuePlan plan)
    {
        var remainingHolds = plan.CollectedHold is { } hold ? Holds.Remove(hold) : Holds;
        var state = new Library(Titles, Members, Loans.Add(plan.Loan), remainingHolds);
        return new Change(state, FormattableString.Invariant(
            $"Issued {plan.Loan.TitleId} to {plan.Loan.MemberId}. Due {plan.Loan.DueOn:yyyy-MM-dd}."));
    }

    public Result<Change> Reserve(string titleId, string memberId) =>
        ResolveBorrower(titleId, memberId).Bind(borrower =>
        {
            if (FindLoan(titleId, memberId) is Lookup<Loan>.Found ||
                Holds.Any(hold => hold.TitleId == titleId && hold.MemberId == memberId))
                return new Result<Change>.Fail("already-active", "The member already borrows or has reserved this title.");

            int queued = QueueLength(titleId);
            if (FreeCopies(borrower.Title) > queued)
                return new Result<Change>.Fail("available-now", "An unreserved copy is available for checkout.");

            return Hold.Create(titleId, memberId).Map(hold =>
                new Change(new Library(Titles, Members, Loans, Holds.Add(hold)),
                    FormattableString.Invariant($"Reserved {titleId} for {memberId}. Queue position {queued + 1}.")));
        });

    public Result<Change> Renew(string titleId, string memberId, DateOnly date) =>
        FindLoan(titleId, memberId).Require("no-active-loan", "Active loan").Bind(loan =>
        {
            if (loan.RenewedOn is not null)
                return new Result<Change>.Fail("renewal-used", "The single renewal has already been used.");
            if (date < loan.OpenedOn || date > loan.DueOn)
                return new Result<Change>.Fail("renewal-date", "Renewal must occur during the original loan period.");
            if (QueueLength(titleId) > 0)
                return new Result<Change>.Fail("waiting-list", "A member is waiting for this title.");

            return Loan.Create(titleId, memberId, loan.OpenedOn, date).Map(renewed =>
                new Change(new Library(Titles, Members, Loans.Replace(loan, renewed), Holds),
                    FormattableString.Invariant($"Renewed {titleId} for {memberId}. Due {renewed.DueOn:yyyy-MM-dd}.")));
        });

    public Result<ReturnPlan> PrepareReturn(string titleId, string memberId, DateOnly date) =>
        FindLoan(titleId, memberId).Require("no-active-loan", "Active loan").Bind<Loan, ReturnPlan>(loan =>
            date < (loan.RenewedOn ?? loan.OpenedOn)
                ? new Result<ReturnPlan>.Fail("return-date", "Return date precedes the last loan event.")
                : new Result<ReturnPlan>.Ok(new ReturnPlan(
                    new Library(Titles, Members, Loans.Remove(loan), Holds), DaysLate(loan, date))));

    public Result<Change> RetireCopy(string titleId) =>
        FindTitle(titleId).Require("unknown-title", "Title").Bind(title =>
        {
            if (title.Copies == 1)
                return new Result<Change>.Fail("last-copy", "The only copy cannot be retired.");
            if (FreeCopies(title) <= QueueLength(titleId))
                return new Result<Change>.Fail("copy-committed", "All copies are checked out or promised to the queue.");

            return Title.Create(title.Id, title.Name, title.Section, title.Copies - 1).Map(updated =>
                new Change(new Library(Titles.Replace(title, updated), Members, Loans, Holds),
                    FormattableString.Invariant($"Retired one copy of {title.Id}. {updated.Copies} remaining.")));
        });

    public static int DaysLate(Loan loan, DateOnly date) =>
        Math.Max(0, date.DayNumber - loan.DueOn.DayNumber);
}
