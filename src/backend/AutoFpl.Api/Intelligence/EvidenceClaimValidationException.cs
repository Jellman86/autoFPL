namespace AutoFpl.Api.Intelligence;

public sealed class EvidenceClaimValidationException : Exception
{
    public EvidenceClaimValidationException(string code, string field)
        : base($"Evidence claim validation failed: {code}.")
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }
}
