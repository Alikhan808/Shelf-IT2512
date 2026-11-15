namespace Shelf.Core;

public abstract record Result<T>
{
    private Result() { }

    public sealed record Ok(T Value) : Result<T>;
    public sealed record Fail(string Code, string Message) : Result<T>;
}

public abstract record Lookup<T>
{
    private Lookup() { }

    public sealed record Found(T Value) : Lookup<T>;
    public sealed record Missing(string Key) : Lookup<T>;
}

public static class ResultExtensions
{
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> source, Func<TIn, TOut> map) =>
        source.Bind(value => new Result<TOut>.Ok(map(value)));

    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> source, Func<TIn, Result<TOut>> next) =>
        source switch
        {
            Result<TIn>.Ok(var value) => next(value),
            Result<TIn>.Fail(var code, var message) => new Result<TOut>.Fail(code, message),
            _ => throw new InvalidOperationException("Unexpected result case.")
        };

    public static Result<T> Require<T>(this Lookup<T> lookup, string code, string label) =>
        lookup switch
        {
            Lookup<T>.Found(var value) => new Result<T>.Ok(value),
            Lookup<T>.Missing(var key) => new Result<T>.Fail(code, $"{label} not found: {key}."),
            _ => throw new InvalidOperationException("Unexpected lookup case.")
        };
}
