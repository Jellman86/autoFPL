using AutoFpl.Api.Persistence;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AutoFpl.Api.Errors;

public sealed class DecisionSnapshotPersistenceExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DecisionSnapshotPersistenceException persistenceException)
        {
            return false;
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Decision snapshot cannot be persisted.",
            Type = $"urn:autofpl:error:{persistenceException.Code}",
        };
        problem.Extensions["code"] = persistenceException.Code;
        problem.Extensions["field"] = persistenceException.Field;

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken: cancellationToken);
        return true;
    }
}
