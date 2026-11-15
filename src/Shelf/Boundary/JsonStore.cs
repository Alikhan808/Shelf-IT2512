using System.Text.Json;
using System.Text.Json.Serialization;
using Shelf.Core;

namespace Shelf.Boundary;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    private sealed record TitleRow([property: JsonRequired] string Id, [property: JsonRequired] string Name,
        [property: JsonRequired] string Section, [property: JsonRequired] int Copies);
    private sealed record MemberRow([property: JsonRequired] string Id, [property: JsonRequired] string Name,
        [property: JsonRequired] string Tier);
    private sealed record LoanRow([property: JsonRequired] string TitleId, [property: JsonRequired] string MemberId,
        [property: JsonRequired] DateOnly OpenedOn, [property: JsonRequired] DateOnly? RenewedOn);
    private sealed record HoldRow([property: JsonRequired] string TitleId, [property: JsonRequired] string MemberId);
    private sealed record Document([property: JsonRequired] int Version, [property: JsonRequired] TitleRow[] Titles,
        [property: JsonRequired] MemberRow[] Members, [property: JsonRequired] LoanRow[] Loans,
        [property: JsonRequired] HoldRow[] Holds);

    public static Result<Library> Load(string path) => Guard(() =>
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var document = ReadDocument(stream);
        if (document is not { Version: 1, Titles: not null, Members: not null, Loans: not null, Holds: not null })
            return new Result<Library>.Fail("invalid-document", "Expected version 1 and four collections.");

        return ValidateRows(document.Titles, row => Title.Create(row.Id, row.Name, row.Section, row.Copies)).Bind(titles =>
            ValidateRows(document.Members, row => Member.Create(row.Id, row.Name, row.Tier)).Bind(members =>
            ValidateRows(document.Loans, row => Loan.Create(row.TitleId, row.MemberId, row.OpenedOn, row.RenewedOn)).Bind(loans =>
            ValidateRows(document.Holds, row => Hold.Create(row.TitleId, row.MemberId)).Bind(holds =>
            Library.Create(titles, members, loans, holds)))));
    });

    private static Document? ReadDocument(Stream stream)
    {
        using var json = JsonDocument.Parse(stream);
        try
        {
            RejectDuplicateProperties(json.RootElement);
            return json.RootElement.Deserialize<Document>(Format);
        }
        catch (InvalidOperationException exception)
        {
            // JsonDocument defers decoding names and strings until they are read.
            throw new JsonException("Invalid text in the JSON document.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    string name = property.Name;
                    if (!names.Add(name))
                        throw new JsonException($"Duplicate JSON property '{name}'.");

                    RejectDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    RejectDuplicateProperties(item);
                break;
        }
    }

    private static Result<List<T>> ValidateRows<TDto, T>(TDto[] rows, Func<TDto, Result<T>> validate)
        where TDto : class
    {
        var values = new List<T>(rows.Length);
        foreach (var row in rows)
        {
            if (row is null)
                return new Result<List<T>>.Fail("invalid-document", "Collections may not contain null entries.");

            switch (validate(row))
            {
                case Result<T>.Ok(var value):
                    values.Add(value);
                    break;
                case Result<T>.Fail(var code, var message):
                    return new Result<List<T>>.Fail(code, message);
            }
        }
        return new Result<List<T>>.Ok(values);
    }

    // Hold the lock across reading, applying the change and replacing the file.
    public static Result<string> Update(string path, Func<Library, Result<Change>> operation) => Guard(() =>
    {
        string target = Path.GetFullPath(path);
        using var writerLock = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        return Load(target).Bind(operation).Bind(change =>
        {
            Save(target, change.State);
            return new Result<string>.Ok(change.Receipt);
        });
    });

    private static void Save(string target, Library state)
    {
        var document = new Document(1,
            state.Titles.Select(title => new TitleRow(title.Id, title.Name, title.Section, title.Copies)).ToArray(),
            state.Members.Select(member => new MemberRow(member.Id, member.Name,
                member.Tier == Membership.Standard ? "standard" : "extended")).ToArray(),
            state.Loans.Select(loan => new LoanRow(loan.TitleId, loan.MemberId, loan.OpenedOn, loan.RenewedOn)).ToArray(),
            state.Holds.Select(hold => new HoldRow(hold.TitleId, hold.MemberId)).ToArray());

        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool ownsTemporary = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                JsonSerializer.Serialize(stream, document, Format);
                stream.Flush(flushToDisk: true);
            }
            File.Replace(temporary, target, null);
            ownsTemporary = false;
        }
        finally
        {
            if (ownsTemporary)
                File.Delete(temporary);
        }
    }

    private static Result<T> Guard<T>(Func<Result<T>> action)
    {
        try
        {
            return action();
        }
        catch (FileNotFoundException exception)
        {
            return new Result<T>.Fail("file-missing", exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            return new Result<T>.Fail("file-missing", exception.Message);
        }
        catch (JsonException exception)
        {
            return new Result<T>.Fail("malformed-json", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return new Result<T>.Fail("file-error", exception.Message);
        }
    }
}
