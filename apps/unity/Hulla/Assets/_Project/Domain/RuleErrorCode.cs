namespace Hulla.Domain
{
    /// <summary>A stable error code from the server or a local policy, with an optional detail.</summary>
    public readonly struct RuleErrorCode
    {
        public RuleErrorCode(string code, string? detail = null)
        {
            Code = code;
            Detail = detail;
        }

        public string Code { get; }

        public string? Detail { get; }

        public override string ToString() => Detail == null ? Code : $"{Code} ({Detail})";
    }
}
