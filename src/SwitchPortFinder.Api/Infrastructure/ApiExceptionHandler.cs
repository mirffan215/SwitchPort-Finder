using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.Api.Infrastructure;

/// <summary>Maps domain exceptions to RFC 7807 problem details. Never includes stack traces, SQL or credentials.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (ex)
        {
            case ValidationFailedException v:
                problem = new ValidationProblemDetails(v.Errors.ToDictionary(k => k.Key, k => k.Value)) { Status = 400, Title = "Validation failed" };
                break;
            case NotFoundException n:
                problem = new ProblemDetails { Status = 404, Title = "Not found", Detail = n.Message };
                break;
            case ConflictException c:
                problem = new ProblemDetails { Status = 409, Title = "Conflict", Detail = c.Message };
                break;
            case OperationCanceledException when ctx.RequestAborted.IsCancellationRequested:
                return true;
            case BadHttpRequestException b:
                problem = new ProblemDetails { Status = 400, Title = "Bad request", Detail = b.Message };
                break;
            default:
                log.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Unexpected error", Detail = "An unexpected error occurred. See the server log." };
                break;
        }
        ctx.Response.StatusCode = problem.Status!.Value;
        await ctx.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
