using AutoFpl.Domain.Snapshots;

using Microsoft.AspNetCore.Diagnostics;

namespace AutoFpl.Api.Errors;

public sealed class DecisionSnapshotValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DecisionSnapshotValidationException validationException)
        {
            return false;
        }

        IResult problem = Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Decision snapshot metadata is invalid.",
            type: $"urn:autofpl:error:{validationException.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = validationException.Code,
                ["field"] = validationException.Field,
            });

        await problem.ExecuteAsync(httpContext);
        return true;
    }
}
