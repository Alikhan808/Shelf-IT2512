# Shelf

Shelf is a C# console application for a small university library. A librarian can check out and return books, extend loans, manage a waiting list, and see how many copies are available. The program saves the current library state in a JSON file.

## Project information

- **Course** Introduction to Functional Programming
- **Group** IT-2512
- **Team** Amirebay Alikhan, Zharkyn Bolat, Sekerhanov Sultan, Baiqonyr Erasyl
- **Repository** [Alikhan808/Shelf-IT2512](https://github.com/Alikhan808/Shelf-IT2512)
- **Branch** `main`
- **Verified source commit** [`91164ed34085fd63a422b5da2783a0603b9a2658`](https://github.com/Alikhan808/Shelf-IT2512/commit/91164ed34085fd63a422b5da2783a0603b9a2658)

## Build and run

The project was tested on Windows x64 with .NET SDK 8.0.425. It uses no external NuGet packages. Open PowerShell in the `Shelf_Source` folder containing `Shelf.sln`.

```powershell
dotnet build Shelf.sln -c Release -m:1
New-Item -ItemType Directory -Force demo | Out-Null
Copy-Item data/sample.json demo/library.json
dotnet run --project src/Shelf -c Release --no-build -- --data demo/library.json sections
```

Use `--data` to choose a JSON file. `data/sample.json` contains example books, members and loans. `data/empty.json` is an empty library. Use a copy of the sample for the demo so you can start again with the same data.

## Commands

Pass these commands after `--data demo/library.json`.

| Command | What it does |
| --- | --- |
| `checkout TITLE MEMBER YYYY-MM-DD campus\|exam` | Lends an available copy for 14 days if the member is below the loan limit. |
| `renew TITLE MEMBER YYYY-MM-DD` | Extends the original due date by 7 days. A loan can be renewed once, during its original period, if nobody is waiting for that title. |
| `return TITLE MEMBER YYYY-MM-DD` | Closes an active loan and shows the late fee. |
| `reserve TITLE MEMBER` | Adds the member to the waiting list when no copy is available for walk-in checkout. |
| `collect TITLE MEMBER YYYY-MM-DD campus\|exam` | Lends an available copy to the first member in the waiting list and removes their reservation. |
| `retire TITLE` | Removes one free, unreserved copy. The library keeps at least one copy of the title. |
| `overdue YYYY-MM-DD all\|week` | Lists active loans that are at least 1 or 7 days overdue. |
| `sections` | Shows total copies, checked-out copies, walk-in availability and waiting-list size by section. |

For example, on a fresh copy of the sample data, M01 already has one active loan. The `exam` policy rejects the first command below. The `campus` policy allows the second and sets the due date to `2026-11-26`.

```powershell
dotnet run --project src/Shelf -c Release --no-build -- --data demo/library.json checkout B201 M01 2026-11-12 exam
dotnet run --project src/Shelf -c Release --no-build -- --data demo/library.json checkout B201 M01 2026-11-12 campus
```

## How the code is organised

- `src/Shelf/Core/Models.cs` defines books, members, loans and reservations. Factory methods validate the input before creating a value.
- `src/Shelf/Core/Circulation.cs` contains the lending rules. Checkout runs through `ResolveBorrower`, `PlanIssue` and `CommitIssue`. `PlanIssue` takes a function that sets the loan limit, so the pipeline works with both policies.
- `src/Shelf/Core/Result.cs` contains `Result` and `Lookup`. `Map`, `Bind` and `Require` help pass successful values through the pipeline and stop it when a step fails.
- `src/Shelf/Core/Reports.cs` builds both reports. `OverdueFrom` is partially applied with thresholds of 1 and 7 days. The iterator uses `yield return`, and the report converts the sorted rows to an array before printing. The sections report groups and sums figures by section.
- `src/Shelf/Boundary/JsonStore.cs` loads and validates JSON. A successful update returns a new library state, which is saved through a temporary file. A lock prevents two copies of the app from writing to the same file at once.
- `src/Shelf/Boundary/NativeFee.cs` connects C# to the C fee function. `src/Shelf/Program.cs` handles command-line arguments and prints the result.

## Native fee library

`shelf_fee.c` calculates the late fee at 100 KZT per day, up to 3,000 KZT. C# calls it through P/Invoke when a loan is returned. If the DLL cannot be loaded, the program reports the error and keeps the loan open.

The Windows x64 DLL is included in `runtimes/win-x64/native/`. The .NET build copies it to the output folder. To build it from source, add Zig 0.15.2 to your PATH and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File native/build.ps1
dotnet build Shelf.sln -c Release -m:1
```

## Tests and demo

Run these commands after the Release build:

```powershell
dotnet run --project tests/Shelf.Tests -c Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/demo.ps1
```

All 31 tests passed on the verified version. They cover loan limits, waiting-list order, date validation, reports on empty data, malformed JSON, interrupted updates and calls to the native library. The demo also shows an expected checkout failure and a return that charges 500 KZT for five late days.

## Limitations

Shelf stores active loans and reservations. When a loan is returned, the app prints the fee and removes the loan from active records. It does not keep payment or return history. To add books or members, edit the JSON file. The included native library targets Windows x64.
