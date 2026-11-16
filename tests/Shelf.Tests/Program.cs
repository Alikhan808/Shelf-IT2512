using System.Diagnostics;
using System.Text.Json;
using Shelf.Boundary;
using Shelf.Core;

var tests = new (string Name, Action Run)[]
{
    ("Domain / title validates boundaries", TitleValidation),
    ("Domain / member tier is explicit", MemberValidation),
    ("Domain / loan rejects overflow and invalid renewal date", LoanValidation),
    ("Domain / orphan activity is rejected", OrphanActivity),
    ("Domain / duplicates and overbooking are rejected", StateValidation),
    ("Domain / immutable snapshot and missing lookup", SnapshotAndLookup),
    ("Domain / same input differs under both policies", PolicyDifference),
    ("Domain / bind and map skip downstream work on failure", StopsOnFailure),
    ("Domain / renewal rules and due-date boundary", RenewalRules),
    ("Domain / no checkout or retirement ahead of queued holds", QueueProtection),
    ("Domain / invalid return dates and absent loans are rejected", ReturnPrerequisites),
    ("Reports / overdue thresholds and deterministic ties", OverdueReport),
    ("Reports / section aggregation and deterministic ties", SectionReport),
    ("Reports / empty state and no matching loans", EmptyReports),
    ("Reports / iterator defers evaluation and snapshot reuses results", LazyProcessing),
    ("Workflow / checkout, renew, return and reject repeat return", LoanLifecycle),
    ("Workflow / queue, return, then collect in FIFO order", QueueLifecycle),
    ("Workflow / retire free inventory and persist reduced capacity", InventoryLifecycle),
    ("File / missing file yields a failure", MissingFile),
    ("File / malformed JSON releases file resources", MalformedFile),
    ("File / required fields, unknown fields and null arrays are rejected", SchemaValidation),
    ("File / valid UTF-8 BOM and repeated names in separate records", Utf8Bom),
    ("File / duplicate JSON properties block reads and updates", DuplicateProperties),
    ("File / invalid Unicode produces a controlled parsing error", InvalidUnicode),
    ("File / semantic validation prevents invalid active state", InvalidFile),
    ("File / rejected change preserves persisted bytes", NoPartialUpdate),
    ("File / writer lock prevents a concurrent update", WriterLock),
    ("Native / real FFI computes zero, rate, cap and large product", RealNativeCall),
    ("Native / fee matches a 9,261-case reference grid", FeeGrid),
    ("Native / C library rejects invalid arguments", NativeRejection),
    ("Native / absent DLL blocks return and preserves persisted bytes", MissingNativeLibrary)
};

int failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void TitleValidation()
{
    Error("invalid-title", Title.Create("B1", "Book", "Computing", 0));
    Error("invalid-title", Title.Create("B1", "Book", "Computing", 1001));
    Error("invalid-title", Title.Create("b1", "Book", "Computing", 1));
    Error("invalid-title", Title.Create("B1", "Book", " Computing", 1));
    Equal(1000, Ok(Title.Create("B1", "Book", "Computing", 1000)).Copies);
}

static void MemberValidation()
{
    Equal(Membership.Extended, Ok(Member.Create("M1", "Member", "extended")).Tier);
    Error("invalid-member", Member.Create("M1", "Member", "admin"));
    Error("invalid-member", Member.Create("M1", "", "standard"));
    Error("invalid-hold", Hold.Create("invalid", "M1"));
}

static void LoanValidation()
{
    Error("invalid-loan", Loan.Create("B1", "M1", Day(1), Day(16)));
    Error("invalid-loan", Loan.Create("B1", "M1", DateOnly.MaxValue.AddDays(-13), null));
    Equal(DateOnly.MaxValue, Ok(Loan.Create("B1", "M1", DateOnly.MaxValue.AddDays(-14), null)).DueOn);
}

static void OrphanActivity()
{
    Error("unknown-reference", Library.Create([Book()], [Reader()], [MakeLoan("M2")], []));
    Error("unknown-reference", Library.Create([Book()], [Reader()], [], [Ok(Hold.Create("B2", "M1"))]));
}

static void StateValidation()
{
    Error("duplicate-id", Library.Create([Book(), Book()], [Reader()], [], []));
    Error("duplicate-activity", Library.Create([Book()], [Reader()], [MakeLoan(), MakeLoan()], []));
    Error("duplicate-activity", Library.Create([Book()], [Reader()], [MakeLoan()], [Ok(Hold.Create("B1", "M1"))]));
    Error("overallocated", Library.Create([Book(copies: 1)], [Reader(), Reader("M2")], [MakeLoan(), MakeLoan("M2")], []));
}

static void SnapshotAndLookup()
{
    Title[] callerTitles = [Book()];
    var state = Ok(Library.Create(callerTitles, [Reader()], [], []));
    callerTitles[0] = Book("B9");
    Equal("B1", Is<Lookup<Title>.Found>(state.FindTitle("B1")).Value.Id);
    Equal("B9", Is<Lookup<Title>.Missing>(state.FindTitle("B9")).Key);
    var after = Ok(state.Checkout("B1", "M1", Day(1), LoanPolicies.Campus)).State;
    Equal(0, state.Loans.Length);
    Equal(1, after.Loans.Length);
}

static void PolicyDifference()
{
    var state = Ok(Library.Create([Book(), Book("B2")], [Reader()], [MakeLoan()], []));
    Equal(2, Ok(state.Checkout("B2", "M1", Day(1), LoanPolicies.Campus)).State.Loans.Length);
    Error("loan-limit", state.Checkout("B2", "M1", Day(1), LoanPolicies.ExamWeek));
    Equal(1, state.Loans.Length);
}

static void StopsOnFailure()
{
    var state = Empty();
    int invocations = 0;
    Error("unknown-title", state.Checkout("B1", "M1", Day(1), _ => ++invocations));
    Result<int> failure = new Result<int>.Fail("first", "First stage failed.");
    Error("first", failure.Map(value => value + ++invocations)
        .Bind(value => new Result<int>.Ok(value + ++invocations)));
    Equal(0, invocations);
}

static void RenewalRules()
{
    var state = Ok(Library.Create([Book(copies: 1)], [Reader(), Reader("M2")], [MakeLoan()], []));
    Error("no-active-loan", state.Renew("B1", "M2", Day(1)));
    Error("renewal-date", state.Renew("B1", "M1", Day(16)));
    var renewed = Ok(state.Renew("B1", "M1", Day(15))).State;
    Equal(Day(22), renewed.Loans[0].DueOn);
    Error("return-date", renewed.PrepareReturn("B1", "M1", Day(14)));
    Error("renewal-used", renewed.Renew("B1", "M1", Day(16)));
    var queued = Ok(state.Reserve("B1", "M2")).State;
    Error("waiting-list", queued.Renew("B1", "M1", Day(15)));
    Error("already-active", queued.Reserve("B1", "M2"));
}

static void QueueProtection()
{
    var state = Ok(Library.Create([Book()], [Reader(), Reader("M2")], [], [Ok(Hold.Create("B1", "M2"))]));
    var next = Ok(state.RetireCopy("B1")).State;
    Error("unavailable", next.Checkout("B1", "M1", Day(1), LoanPolicies.Campus));
    Error("use-collect", next.Checkout("B1", "M2", Day(1), LoanPolicies.Campus));
    Error("last-copy", next.RetireCopy("B1"));
    var committed = Ok(Library.Create([Book()], [Reader(), Reader("M2")], [MakeLoan()],
        [Ok(Hold.Create("B1", "M2"))]));
    Error("copy-committed", committed.RetireCopy("B1"));
    Error("available-now", state.Reserve("B1", "M1"));
}

static void ReturnPrerequisites()
{
    var state = Ok(Library.Create([Book()], [Reader()], [MakeLoan()], []));
    Error("no-active-loan", state.PrepareReturn("B1", "M2", Day(1)));
    Error("return-date", state.PrepareReturn("B1", "M1", Day(1).AddDays(-1)));
    Equal(0, Ok(state.PrepareReturn("B1", "M1", Day(15))).DaysLate);
    Equal(1, state.Loans.Length);
}

static void OverdueReport()
{
    var state = Ok(Library.Create([Book(), Book("B2")], [Reader(), Reader("M2")],
        [MakeLoan("M2", "B2"), MakeLoan("M2"), MakeLoan(), MakeLoan("M1", "B2", Day(7))], []));
    var all = Reports.Overdue(state, Day(22), weekOnly: false);
    Sequence(["B1/M1/7", "B1/M2/7", "B2/M2/7", "B2/M1/1"],
        all.Select(row => $"{row.TitleId}/{row.MemberId}/{row.DaysLate}"));
    Equal(3, Reports.Overdue(state, Day(22), weekOnly: true).Length);
}

static void SectionReport()
{
    var state = Ok(Library.Create([Book(section: "Z"), Book("B2", "A", 1), Book("B3", "Z", 1)],
        [Reader(), Reader("M2")], [MakeLoan()], [Ok(Hold.Create("B1", "M2"))]));
    Sequence([new SectionRow("A", 1, 0, 1, 0), new SectionRow("Z", 3, 1, 1, 1)],
        Reports.Availability(state));
}

static void EmptyReports()
{
    Equal(0, Reports.Availability(Empty()).Length);
    Equal(0, Reports.Overdue(Empty(), Day(20), weekOnly: false).Length);
    var state = Ok(Library.Create([Book()], [Reader()], [MakeLoan()], []));
    Equal(0, Reports.Overdue(state, Day(15), weekOnly: false).Length);
}

static void LazyProcessing()
{
    int reads = 0;
    IEnumerable<Loan> Source()
    {
        reads++;
        yield return MakeLoan();
    }

    var pending = Reports.AnyOverdue(Day(16))(Source());
    Equal(0, reads);
    var rows = pending.ToArray();
    Equal(1, reads);
    Equal(1, rows.Single().DaysLate);
    Equal("B1", rows.Single().TitleId);
    Equal(1, reads);
}

static void LoanLifecycle() => InTemp(folder =>
{
    string path = Seed(folder);
    Equal("Issued B2 to M1. Due 2026-11-15.",
        Ok(JsonStore.Update(path, state => state.Checkout("B2", "M1", Day(1), LoanPolicies.Campus))));
    Equal("Renewed B2 for M1. Due 2026-11-22.",
        Ok(JsonStore.Update(path, state => state.Renew("B2", "M1", Day(15)))));
    Equal("Returned B2 from M1. 3 day(s) late. Fee 300 KZT.",
        Ok(JsonStore.Update(path, state => NativeFee.Return(state, "B2", "M1", Day(25)))));
    Equal(1, Ok(JsonStore.Load(path)).Loans.Length);
    Error("no-active-loan", JsonStore.Update(path, state => NativeFee.Return(state, "B2", "M1", Day(25))));
});

static void QueueLifecycle() => InTemp(folder =>
{
    string path = Seed(folder);
    Equal("Reserved B1 for M2. Queue position 1.", Ok(JsonStore.Update(path, state => state.Reserve("B1", "M2"))));
    Equal("Reserved B1 for M3. Queue position 2.", Ok(JsonStore.Update(path, state => state.Reserve("B1", "M3"))));
    Error("unavailable", JsonStore.Update(path, state => state.CollectHold("B1", "M2", Day(20), LoanPolicies.Campus)));
    Ok(JsonStore.Update(path, state => NativeFee.Return(state, "B1", "M1", Day(20))));
    Error("not-first-in-queue", JsonStore.Update(path, state => state.CollectHold("B1", "M3", Day(20), LoanPolicies.Campus)));
    Equal("Issued B1 to M2. Due 2026-12-04.",
        Ok(JsonStore.Update(path, state => state.CollectHold("B1", "M2", Day(20), LoanPolicies.Campus))));
    var state = Ok(JsonStore.Load(path));
    Equal("M3", state.Holds.Single().MemberId);
    Equal("M2", state.Loans.Single().MemberId);
});

static void InventoryLifecycle() => InTemp(folder =>
{
    string path = Seed(folder);
    Ok(JsonStore.Update(path, state => state.RetireCopy("B2")));
    var state = Ok(JsonStore.Load(path));
    Equal(1, Is<Lookup<Title>.Found>(state.FindTitle("B2")).Value.Copies);
    Error("last-copy", JsonStore.Update(path, next => next.RetireCopy("B2")));
});

static void MissingFile() => InTemp(folder =>
    Error("file-missing", JsonStore.Load(Path.Combine(folder, "missing.json"))));

static void MalformedFile() => InTemp(folder =>
{
    string path = Path.Combine(folder, "bad.json");
    File.WriteAllText(path, "{ broken ");
    Error("malformed-json", JsonStore.Load(path));
    using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    Equal(9L, exclusive.Length);
});

static void SchemaValidation() => InTemp(folder =>
{
    string path = Path.Combine(folder, "bad.json");
    File.WriteAllText(path, """{"version":1,"titles":[],"members":[],"loans":[]}""");
    Error("malformed-json", JsonStore.Load(path));
    File.WriteAllText(path, """{"version":1,"titles":[],"members":[],"loans":[],"holds":[],"extra":0}""");
    Error("malformed-json", JsonStore.Load(path));
    File.WriteAllText(path, """{"version":1,"titles":[],"members":[],"loans":[],"holds":null}""");
    Error("invalid-document", JsonStore.Load(path));
});

static void InvalidFile() => InTemp(folder =>
{
    string path = Seed(folder);
    File.WriteAllText(path, File.ReadAllText(path).Replace("M1", "M99").Replace("\"id\":\"M99\"", "\"id\":\"M1\""));
    Error("unknown-reference", JsonStore.Load(path));
});

static void Utf8Bom() => InTemp(folder =>
{
    string path = Seed(folder);
    File.WriteAllText(path, File.ReadAllText(path), new System.Text.UTF8Encoding(true));
    Equal(2, Ok(JsonStore.Load(path)).Titles.Length);
});

static void DuplicateProperties() => InTemp(folder =>
{
    string path = Seed(folder);
    string source = File.ReadAllText(path);

    foreach (string ambiguous in new[]
    {
        source.Replace("\"version\":1", "\"version\":0,\"version\":1"),
        source.Replace("\"copies\":1", "\"cop\\u0069es\":0,\"copies\":1")
    })
    {
        File.WriteAllText(path, ambiguous);
        byte[] original = File.ReadAllBytes(path);
        Error("malformed-json", JsonStore.Load(path));
        Error("malformed-json", JsonStore.Update(path,
            _ => throw new InvalidOperationException("Invalid JSON reached the operation.")));
        Sequence(original, File.ReadAllBytes(path));
    }
});

static void InvalidUnicode() => InTemp(folder =>
{
    string path = Seed(folder);
    string source = File.ReadAllText(path);

    File.WriteAllText(path, source.Replace("\"copies\"", "\"\\uD800\""));
    Error("malformed-json", JsonStore.Load(path));

    File.WriteAllText(path, source.Replace("\"Book\"", "\"\\uD800\""));
    Error("malformed-json", JsonStore.Load(path));
});

static void NoPartialUpdate() => InTemp(folder =>
{
    string path = Seed(folder);
    byte[] before = File.ReadAllBytes(path);
    Error("return-date", JsonStore.Update(path, state => NativeFee.Return(state, "B1", "M1", Day(1).AddDays(-1))));
    Sequence(before, File.ReadAllBytes(path));
    Sequence(["state.json", "state.json.lock"],
        Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    using var exclusive = File.Open(path + ".lock", FileMode.Open, FileAccess.Write, FileShare.None);
});

static void WriterLock() => InTemp(folder =>
{
    string path = Seed(folder);
    byte[] before = File.ReadAllBytes(path);
    using var exclusive = File.Open(path + ".lock", FileMode.CreateNew, FileAccess.Write, FileShare.None);
    Error("file-error", JsonStore.Update(path, state => state.RetireCopy("B2")));
    Sequence(before, File.ReadAllBytes(path));
});

static void RealNativeCall()
{
    Equal(0, Ok(NativeFee.Quote(0, 100, 3000)));
    Equal(300, Ok(NativeFee.Quote(3, 100, 3000)));
    Equal(3000, Ok(NativeFee.Quote(30, 100, 3000)));
    Equal(3000, Ok(NativeFee.Quote(31, 100, 3000)));
    Equal(3000, Ok(NativeFee.Quote(int.MaxValue, int.MaxValue, 3000)));
}

static void NativeRejection()
{
    Error("native-fee-rejected", NativeFee.Quote(-1, 100, 3000));
    Error("native-fee-rejected", NativeFee.Quote(1, -100, 3000));
    Error("native-fee-rejected", NativeFee.Quote(1, 100, -1));
}

static void FeeGrid()
{
    for (int days = 0; days <= 20; days++)
    for (int rate = 0; rate <= 20; rate++)
    for (int cap = 0; cap <= 400; cap += 20)
        Equal(Math.Min(days * rate, cap), Ok(NativeFee.Quote(days, rate, cap)));
}

static void MissingNativeLibrary() => InTemp(folder =>
{
    string path = Seed(folder);
    byte[] before = File.ReadAllBytes(path);
    string assembly = Path.Combine(folder, "Shelf.dll");
    File.Copy(typeof(NativeFee).Assembly.Location, assembly);
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = folder,
        UseShellExecute = false,
        RedirectStandardError = true
    };
    foreach (string token in new[] { "exec", "--runtimeconfig",
        Path.Combine(AppContext.BaseDirectory, "Shelf.Tests.runtimeconfig.json"),
        assembly, "--data", path, "return", "B1", "M1", "2026-11-20" })
        start.ArgumentList.Add(token);

    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
    var error = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(15000))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        throw new InvalidOperationException("CLI timeout.");
    }

    Equal(1, process.ExitCode);
    Equal(true, error.GetAwaiter().GetResult().Contains("[native-unavailable]", StringComparison.Ordinal));
    Sequence(before, File.ReadAllBytes(path));
});

static DateOnly Day(int day) => new(2026, 11, day);
static Title Book(string id = "B1", string section = "Computing", int copies = 2) =>
    Ok(Title.Create(id, "Book", section, copies));
static Member Reader(string id = "M1") => Ok(Member.Create(id, "Reader", "standard"));
static Loan MakeLoan(string member = "M1", string title = "B1", DateOnly? opened = null) =>
    Ok(Loan.Create(title, member, opened ?? Day(1), null));
static Library Empty() => Ok(Library.Create([], [], [], []));

static string Seed(string folder)
{
    string path = Path.Combine(folder, "state.json");
    File.WriteAllText(path, """
        {"version":1,"titles":[{"id":"B1","name":"Book","section":"Computing","copies":1},{"id":"B2","name":"Other","section":"Computing","copies":2}],
        "members":[{"id":"M1","name":"First","tier":"standard"},{"id":"M2","name":"Second","tier":"extended"},{"id":"M3","name":"Third","tier":"standard"}],
        "loans":[{"titleId":"B1","memberId":"M1","openedOn":"2026-11-01","renewedOn":null}],"holds":[]}
        """);
    return path;
}

static void InTemp(Action<string> test)
{
    var folder = Directory.CreateTempSubdirectory("shelf-test-");
    try { test(folder.FullName); }
    finally { folder.Delete(recursive: true); }
}

static T Ok<T>(Result<T> result) =>
    result is Result<T>.Ok(var value) ? value : throw new InvalidOperationException($"Expected success, received {result}.");

static void Error<T>(string code, Result<T> result) => Equal(code, Is<Result<T>.Fail>(result).Code);

static T Is<T>(object value) =>
    value is T typed ? typed : throw new InvalidOperationException($"Expected {typeof(T).Name}, received {value}.");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, received {actual}.");
}

static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], received [{string.Join(", ", actual)}].");
}

