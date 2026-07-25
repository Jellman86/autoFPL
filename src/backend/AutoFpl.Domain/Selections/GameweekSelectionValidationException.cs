namespace AutoFpl.Domain.Selections;

public sealed class GameweekSelectionValidationException(
    string code,
    string field) : Exception(code)
{
    public string Code { get; } = code;

    public string Field { get; } = field;
}
