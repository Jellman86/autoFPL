using AutoFpl.Domain.Squads;

using Microsoft.AspNetCore.Diagnostics;

namespace AutoFpl.Api.Errors;

public sealed class SquadValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not SquadValidationException validationException)
        {
            return false;
        }

        IResult problem = Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Squad is infeasible.",
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
