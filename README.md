# Shelf library circulation

Shelf helps a small campus library issue books, manage waiting lists and see what is available. It runs in a console and stores a single JSON snapshot.

- **Course** Introduction to Functional Programming · **Group** IT-2512
- **Team** Amirebay Alikhan, Zharkyn Bolat, Sekerkhanov Sultan, Baiqonyr Erasyl

## Build and run

Use Windows x64, PowerShell and the x64 .NET SDK 8.0.425 (or a later patch in the same SDK feature band). Run the commands from the repository root. No NuGet packages are needed. The checked-in x64 DLL is copied beside both managed entry points by MSBuild. A path containing spaces works when quoted.

```powershell
dotnet --version
dotnet build Shelf.sln -c Release -m:1
New-Item -ItemType Directory -Force demo | Out-Null
Copy-Item data/sample.json demo/library.json
dotnet run --project src/Shelf -c Release --no-build -- --data demo/library.json sections
dotnet run --project src/Shelf -c Release --no-build -- --help
dotnet run --project tests/Shelf.Tests -c Release --no-build
```

The last command runs 31 automated tests, including JSON validation, complete workflows, both reports and calls to the actual native library. The executable assertion suite prints each result and returns 0 on success or 1 if any test fails. To run the scripted console demonstration after building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1
```

To rebuild the DLL, extract [Zig 0.15.2 Windows x86_64](https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip), add its directory to PATH, or pass the full path to zig.exe with `-Zig`. Run the managed build again after any C change.

```powershell
zig version
powershell -NoProfile -ExecutionPolicy Bypass -File native/build.ps1
dotnet build Shelf.sln -c Release -m:1
dotnet run --project tests/Shelf.Tests -c Release --no-build
```

The build script calls Zig's C compiler in C17 mode and targets x86_64 Windows GNU. The ABI is C calling convention with signed 32-bit arguments and result, and an internal 64-bit product. The binding searches beside the managed assembly for `shelf_fee.dll`. If that library or export is unavailable, the CLI reports `native-unavailable` and keeps the loan open.

## Commands and rules

Prefix each command below with `dotnet run --project src/Shelf -c Release --no-build -- --data demo/library.json`.

| Command | Business result |
| --- | --- |
| `checkout TITLE MEMBER YYYY-MM-DD campus\|exam` | Issues an unpromised copy for 14 days if the member is below the selected loan limit. |
| `renew TITLE MEMBER YYYY-MM-DD` | Adds seven days to the original due date, once, during the initial period and with no waiting list. |
| `return TITLE MEMBER YYYY-MM-DD` | Checks chronology, quotes the late fee via the C DLL, then closes the active loan. The fee is 100 KZT per day, capped at 3,000 KZT. |
| `reserve TITLE MEMBER` | Adds a member to the FIFO queue when no walk-in copy is available. A member can have one active loan or one reservation for that title. |
| `collect TITLE MEMBER YYYY-MM-DD campus\|exam` | Issues to the first queued member once a copy is physically available and the policy allows it. It removes the hold in the same transition. |
| `retire TITLE` | Reduces capacity by one if a spare copy is free and unpromised. It preserves at least one catalog copy. |
| `overdue YYYY-MM-DD all\|week` | Lists active loans at least one or seven days late, respectively. Ties are sorted by title and member IDs. |
| `sections` | Groups titles by section and sums total stock, loans, unpromised stock and queue length. Ties use ordinal section name. |

On two identical copies of the sample, try `checkout B201 M01 2026-11-12 exam` and then `checkout B201 M01 2026-11-12 campus`. M01 already has one loan. Exam rejects with `loan-limit`. Campus issues B201 with due date 2026-11-26. Campus limits for standard/extended members are 2/4. Exam limits are 1/2. The dates are explicit inputs rather than today's system date. The overdue report applies a cutoff to the current snapshot.

## Source map

- `Core/Models.cs` and `Core/Library.cs` validate local fields and relationships. Immutable collections preserve the caller's snapshot.
- `Core/Circulation.cs` contains `ResolveBorrower → PlanIssue → CommitIssue`, the other transitions and both borrowing policies. `Core/Result.cs` contains `Result`, `Lookup` and the used `Map`, `Bind` and `Require` extensions.
- `Core/Reports.cs` partially applies `OverdueFrom` with thresholds of 1 and 7. `StreamOverdue` defers enumeration, and `Overdue` materialises the sorted array. `Availability` groups the same data by section.
- `Boundary/JsonStore.cs` owns file streams and deserialises version 1 JSON. `Boundary/NativeFee.cs` owns the managed interop boundary. `Program.cs` parses arguments, dispatches operations and prints results.
- `tests/Shelf.Tests/Program.cs` asserts domain results, complete persisted workflows, reports, file errors and real native execution.

## State files and limitations

Use `--data` to select an existing file. Samples are `data/sample.json` and `data/empty.json`. JSON has four required arrays (titles, members, loans and holds) with unique IDs and valid references. A property name may appear once per JSON object. The reader accepts UTF-8 with or without a byte order mark. For an active loan, `renewedOn` is either an ISO date or `null`. Queue order follows the order of holds in the file.

A writer holds an exclusive sidecar `.lock` stream across load, calculation and replacement. The lock file remains beside the JSON and its open handle controls ownership. The app writes a unique temporary file in the target directory, flushes and closes it, then replaces the original. A failed domain or native step never reaches the save. Reports reuse an in-memory snapshot with no open data stream. Use a local writable NTFS directory and a consistent path for each dataset. File locking covers Shelf processes using the same path. A lock error can be retried.

The demo stores current activity. A returned loan and its fee are not saved as history. Holds remain until collected, and each borrower manages a title by ID instead of a physical barcode. New members and titles are supplied through validated JSON. There is no payment collection, notification, authentication or historical reporting.

.
