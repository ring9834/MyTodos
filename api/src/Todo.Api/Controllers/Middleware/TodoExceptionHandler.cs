using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Todo.Domain.Exceptions;

namespace Todo.Api.Middleware;

/// <summary>
/// The one place exceptions become HTTP responses (ADR-0008 §3.5).
/// <list type="bullet">
/// <item>Writes RFC 9457 ProblemDetails through the framework's <see cref="IProblemDetailsService"/>
/// (registered by <c>AddProblemDetails()</c>), not hand-rolled JSON, so <c>CustomizeProblemDetails</c>
/// (which strips <c>traceId</c>, §3.2) and content negotiation (<c>application/problem+json</c>) apply.</item>
/// <item>Adds a stable, machine-readable <c>code</c>; the UI branches on it, never on message text (§3.6).</item>
/// <item>Never exposes framework exception messages; only domain-authored messages reach the client (NFR-3).</item>
/// <item>Contains the ONLY <c>ILogger</c> call in application code (§3.1); everything else comes from OpenTelemetry.</item>
/// </list>
/// </summary>
public sealed class TodoExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<TodoExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        // The client went away: not an error, and there is nobody to write to (§3.5).
        var clientAborted = exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested;
        var outcome = clientAborted ? Outcome.ClientAborted : Map(exception);

        // The single ILogger call in application code (§3.1). The level comes from the mapping table;
        // the framework's own diagnostics for handled exceptions are suppressed in Program.cs, so this
        // is the only record. The route TEMPLATE is logged, never bodies or headers (NFR-6).
        logger.Log(outcome.Level, exception, "Request failed: {Status} {Code} on {Route}",
            outcome.Status, outcome.Code, RouteOf(http));

        if (clientAborted)
            return true;

        var problem = new ProblemDetails
        {
            Status = outcome.Status,
            Title = outcome.Title,
            Detail = outcome.Detail,
            Instance = http.Request.Path,
            // `type` is left to AddProblemDetails' defaults (the RFC 9110 section for the status).
        };
        problem.Extensions["code"] = outcome.Code;
        if (exception is ArgumentException argument && outcome.Status == StatusCodes.Status400BadRequest
            && FieldErrors(argument) is { } errors)
        {
            problem.Extensions["errors"] = errors;
        }

        http.Response.StatusCode = outcome.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    // Specific before general (§3.5). Only the concurrency subtype of DbUpdateException is a 409;
    // any other database failure is unexpected and falls through to 500.
    private static Outcome Map(Exception exception) => exception switch
    {
        // Illegal state-machine transition, or editing a done item (Q2, BR-3). The request was
        // well-formed but conflicts with the resource's current state. The domain authors this
        // message, so it is safe to show.
        InvalidTodoTransitionException e => new(StatusCodes.Status409Conflict, "todo.invalid_transition",
            "This change isn't allowed.", e.Message, LogLevel.Information),

        // Someone else changed the row first (stale If-Match / xmin; Q6). Never retried automatically
        // (ADR-0009 §3.6): surfaced so the client can reload and decide.
        DbUpdateConcurrencyException => new(StatusCodes.Status409Conflict, "todo.version_conflict",
            "Conflict", "This item was changed elsewhere. Reload and try again.", LogLevel.Warning),

        // Missing, or exists but belongs to someone else: the same 404 either way, so existence never
        // leaks (Q1). Fixed text: KeyNotFoundException messages can contain keys.
        KeyNotFoundException => new(StatusCodes.Status404NotFound, "todo.not_found",
            "Not found", "The item was not found.", LogLevel.Information),

        // Domain guard clauses (BR-1, BR-5; §3.7). Field detail goes in `errors`, keyed by ParamName.
        ArgumentException => new(StatusCodes.Status400BadRequest, "validation_failed",
            "Invalid request", "One or more fields are invalid.", LogLevel.Information),

        // Anything else is a genuine bug or an infrastructure failure. A generic message only:
        // the details stay in the log record above.
        _ => new(StatusCodes.Status500InternalServerError, "server_error",
            "Something went wrong.", null, LogLevel.Error),
    };

    // errors: { "<requestField>": ["<message>"] }. The guard's ParamName matches the request field
    // (e.g. "scheduledFor"). The framework's " (Parameter 'x')" suffix is removed, and so is anything
    // after it, such as ArgumentOutOfRangeException's "Actual value was …", which would echo user data.
    private static Dictionary<string, string[]>? FieldErrors(ArgumentException exception)
    {
        if (string.IsNullOrWhiteSpace(exception.ParamName))
            return null;

        var field = JsonNamingPolicy.CamelCase.ConvertName(exception.ParamName);
        var cut = exception.Message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        var message = cut >= 0 ? exception.Message[..cut] : exception.Message;

        return new Dictionary<string, string[]> { [field] = [message] };
    }

    // The route template (e.g. "api/todos/{id:guid}/state") groups requests in telemetry and never
    // contains user data.
    private static string RouteOf(HttpContext http) =>
        (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";

    private readonly record struct Outcome(int Status, string Code, string Title, string? Detail, LogLevel Level)
    {
        public static readonly Outcome ClientAborted =
            new(499, "client_aborted", "Client closed request", null, LogLevel.Debug);
    }
}