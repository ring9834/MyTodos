using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Todo.Domain.Exceptions;

namespace Todo.Api.Middleware;

// Maps domain/infrastructure exceptions to RFC 9457 ProblemDetails (openapi.yaml).
// Uses ASP.NET Core's built-in IExceptionHandler + AddProblemDetails() — not hand-rolled
// middleware, per "use the framework rather than reinventing it".
public class TodoExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            // Q1: not found, or found but not owned by the caller — 404 either way, never
            // leak which case it was.
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),

            // Q2: illegal state transition -> 409, not 400 — the request was
            // well-formed, it just conflicts with the resource's current state.
            InvalidTodoTransitionException => (StatusCodes.Status409Conflict, "Invalid state transition"),

            // Q6: someone else already changed this row (xmin mismatch) -> 409.
            // NEVER retried automatically (ADR-0023) — surfaced to the client to decide.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict"),

            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail = exception.Message,
            instance = httpContext.Request.Path.Value
        }, ct);

        return true;
    }
}