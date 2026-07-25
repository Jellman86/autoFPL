namespace AutoFpl.Api.Persistence;

public sealed class DecisionSnapshotPersistenceException : Exception
{
    public DecisionSnapshotPersistenceException(string code, string field)
        : base($"Decision snapshot persistence failed for '{field}' ({code}).")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
