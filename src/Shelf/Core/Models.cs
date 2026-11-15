namespace Shelf.Core;

public enum Membership { Standard, Extended }

internal static class Input
{
    internal static bool IsId(string? value) =>
        value is { Length: >= 1 and <= 16 } &&
        value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    internal static bool IsLabel(string? value) =>
        value is { Length: >= 1 and <= 80 } && value == value.Trim() && !value.Any(char.IsControl);
}

public sealed class Title
{
    public string Id { get; }
    public string Name { get; }
    public string Section { get; }
    public int Copies { get; }

    private Title(string id, string name, string section, int copies) =>
        (Id, Name, Section, Copies) = (id, name, section, copies);

    public static Result<Title> Create(string id, string name, string section, int copies) =>
        Input.IsId(id) && Input.IsLabel(name) && Input.IsLabel(section) && copies is >= 1 and <= 1000
            ? new Result<Title>.Ok(new Title(id, name, section, copies))
            : new Result<Title>.Fail("invalid-title", $"Invalid catalog title: {id}.");
}

public sealed class Member
{
    public string Id { get; }
    public string Name { get; }
    public Membership Tier { get; }

    private Member(string id, string name, Membership tier) => (Id, Name, Tier) = (id, name, tier);

    public static Result<Member> Create(string id, string name, string tier) =>
        (Input.IsId(id), Input.IsLabel(name), tier) switch
        {
            (true, true, "standard") => new Result<Member>.Ok(new Member(id, name, Membership.Standard)),
            (true, true, "extended") => new Result<Member>.Ok(new Member(id, name, Membership.Extended)),
            _ => new Result<Member>.Fail("invalid-member", $"Invalid library member: {id}.")
        };
}

public sealed class Loan
{
    public const int InitialDays = 14;
    public const int ExtensionDays = 7;
    public string TitleId { get; }
    public string MemberId { get; }
    public DateOnly OpenedOn { get; }
    public DateOnly? RenewedOn { get; }
    public DateOnly DueOn => OpenedOn.AddDays(InitialDays + (RenewedOn is null ? 0 : ExtensionDays));

    private Loan(string titleId, string memberId, DateOnly openedOn, DateOnly? renewedOn) =>
        (TitleId, MemberId, OpenedOn, RenewedOn) = (titleId, memberId, openedOn, renewedOn);

    public static Result<Loan> Create(string titleId, string memberId, DateOnly openedOn, DateOnly? renewedOn) =>
        Input.IsId(titleId) && Input.IsId(memberId) &&
        openedOn.DayNumber <= DateOnly.MaxValue.DayNumber - InitialDays - (renewedOn is null ? 0 : ExtensionDays) &&
        (renewedOn is null || (renewedOn >= openedOn && renewedOn <= openedOn.AddDays(InitialDays)))
            ? new Result<Loan>.Ok(new Loan(titleId, memberId, openedOn, renewedOn))
            : new Result<Loan>.Fail("invalid-loan", $"Invalid loan for {titleId} and {memberId}.");
}

public sealed class Hold
{
    public string TitleId { get; }
    public string MemberId { get; }

    private Hold(string titleId, string memberId) => (TitleId, MemberId) = (titleId, memberId);

    public static Result<Hold> Create(string titleId, string memberId) =>
        Input.IsId(titleId) && Input.IsId(memberId)
            ? new Result<Hold>.Ok(new Hold(titleId, memberId))
            : new Result<Hold>.Fail("invalid-hold", $"Invalid reservation for {titleId} and {memberId}.");
}
