using System.Globalization;
using Shelf.Boundary;
using Shelf.Core;

if (args.Length == 0 || args is ["--help"])
{
    Console.WriteLine("""
        Shelf library circulation
        dotnet run --project src/Shelf -- --data PATH COMMAND

        checkout TITLE MEMBER YYYY-MM-DD campus|exam
        renew    TITLE MEMBER YYYY-MM-DD
        return   TITLE MEMBER YYYY-MM-DD
        reserve  TITLE MEMBER
        collect  TITLE MEMBER YYYY-MM-DD campus|exam
        retire   TITLE
        overdue  YYYY-MM-DD all|week
        sections
        """);
    return 0;
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

if (args is not ["--data", var path, var command, .. var tokens])
    return Failure("usage", "Expected --data PATH COMMAND. Use --help for syntax.", 2);

switch (command, tokens)
{
    case ("checkout" or "collect", [var title, var member, var dateText, "campus" or "exam"]):
        if (!TryDate(dateText, out var issueDate))
            return Failure("invalid-date", "Use YYYY-MM-DD.", 2);
        Func<Member, int> limit = tokens[3] == "campus" ? LoanPolicies.Campus : LoanPolicies.ExamWeek;
        return Print(JsonStore.Update(path, state => command == "checkout"
            ? state.Checkout(title, member, issueDate, limit)
            : state.CollectHold(title, member, issueDate, limit)));

    case ("renew" or "return", [var title, var member, var dateText]):
        if (!TryDate(dateText, out var loanDate))
            return Failure("invalid-date", "Use YYYY-MM-DD.", 2);
        return Print(JsonStore.Update(path, state => command == "renew"
            ? state.Renew(title, member, loanDate)
            : NativeFee.Return(state, title, member, loanDate)));

    case ("reserve", [var title, var member]):
        return Print(JsonStore.Update(path, state => state.Reserve(title, member)));

    case ("retire", [var title]):
        return Print(JsonStore.Update(path, state => state.RetireCopy(title)));

    case ("overdue", [var dateText, "all" or "week"]):
        if (!TryDate(dateText, out var reportDate))
            return Failure("invalid-date", "Use YYYY-MM-DD.", 2);
        return Print(JsonStore.Load(path).Map(state => FormatReport(
            Reports.Overdue(state, reportDate, tokens[1] == "week"),
            "Title | Member | Due date | Days late",
            row => $"{row.TitleId} | {row.MemberId} | {row.DueOn:yyyy-MM-dd} | {row.DaysLate}")));

    case ("sections", []):
        return Print(JsonStore.Load(path).Map(state => FormatReport(
            Reports.Availability(state), "Section | Total | Out | Walk-in | Queue",
            row => $"{row.Section} | {row.Copies} | {row.CheckedOut} | {row.WalkInCopies} | {row.Waiting}")));

    default:
        return Failure("usage", "Invalid command or arguments. Use --help for syntax.", 2);
}

static bool TryDate(string text, out DateOnly date) =>
    DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

static string FormatReport<T>(T[] rows, string header, Func<T, string> format) =>
    rows.Length == 0 ? "No matching records." : header + Environment.NewLine + string.Join(Environment.NewLine, rows.Select(format));

static int Print(Result<string> result)
{
    switch (result)
    {
        case Result<string>.Ok(var value):
            Console.WriteLine(value);
            return 0;
        case Result<string>.Fail(var code, var message):
            return Failure(code, message, 1);
        default:
            throw new InvalidOperationException("Unexpected result case.");
    }
}

static int Failure(string code, string message, int exitCode)
{
    Console.Error.WriteLine($"[{code}] {message}");
    return exitCode;
}
