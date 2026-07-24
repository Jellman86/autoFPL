namespace AutoFpl.Domain.Snapshots;

public sealed class DecisionSnapshotValidationException : Exception
{
    public DecisionSnapshotValidationException(string code, string field)
        : base($"Decision snapshot validation failed for '{field}' ({code}).")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
