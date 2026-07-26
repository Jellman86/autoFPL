using AutoFpl.Api.Selections;

using Microsoft.AspNetCore.Diagnostics;

namespace AutoFpl.Api.Errors;

public sealed class SelectionWorkflowExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not SelectionWorkflowException workflowException)
        {
            return false;
        }

        IResult problem = Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Selection action cannot be completed.",
            type: $"urn:autofpl:error:{workflowException.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = workflowException.Code,
                ["field"] = workflowException.Field,
            });

        await problem.ExecuteAsync(httpContext);
        return true;
    }
}
