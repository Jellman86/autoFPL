namespace AutoFpl.Domain.Outcomes;

public sealed class GameweekSubstitutionResolutionValidationException(
    string code,
    string field) : Exception(code)
{
    public string Code { get; } = code;

    public string Field { get; } = field;
}
