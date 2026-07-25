using AutoFpl.Domain.Outcomes;

using Microsoft.AspNetCore.Diagnostics;

namespace AutoFpl.Api.Errors;

public sealed class GameweekScoreResolutionValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not GameweekScoreResolutionValidationException validationException)
        {
            return false;
        }

        IResult problem = Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Manual Gameweek points evidence is invalid.",
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
