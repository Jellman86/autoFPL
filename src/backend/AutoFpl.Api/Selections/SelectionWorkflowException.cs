namespace AutoFpl.Api.Selections;

public sealed class SelectionWorkflowException : Exception
{
    public SelectionWorkflowException(string code, string field)
        : base($"Selection workflow failed for '{field}' ({code}).")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
