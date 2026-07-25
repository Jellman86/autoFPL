namespace AutoFpl.Domain.Lineups;

public sealed class LineupValidationException : Exception
{
    public LineupValidationException(string code, string field)
        : base(code)
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
