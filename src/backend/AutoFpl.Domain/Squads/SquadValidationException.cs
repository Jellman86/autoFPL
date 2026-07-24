namespace AutoFpl.Domain.Squads;

public sealed class SquadValidationException : Exception
{
    public SquadValidationException(string code, string field)
        : base(code)
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
