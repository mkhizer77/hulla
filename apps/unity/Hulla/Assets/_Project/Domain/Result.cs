using System;

namespace Hulla.Domain
{
    /// <summary>A value or an error, used instead of exceptions for expected failures.</summary>
    public readonly struct Result<T>
    {
        private readonly T _value;

        private Result(T value, RuleErrorCode error, bool ok)
        {
            _value = value;
            Error = error;
            IsOk = ok;
        }

        public bool IsOk { get; }

        public RuleErrorCode Error { get; }

        public T Value => IsOk ? _value : throw new InvalidOperationException($"Result is an error: {Error}");

        public static Result<T> Ok(T value) => new Result<T>(value, default, true);

        public static Result<T> Fail(RuleErrorCode error) => new Result<T>(default!, error, false);

        public static Result<T> Fail(string code, string? detail = null) => Fail(new RuleErrorCode(code, detail));
    }
}
