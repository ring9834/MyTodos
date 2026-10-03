using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Shouldly;
using Todo.Api.Middleware;
using Todo.Domain.Entities;
using Todo.Domain.Exceptions;

namespace Todo.UnitTests.Api;

// ADR-0008 §6: "Unit test over the exception → (status, code) table".
public class TodoExceptionHandlerTests
{
    public static TheoryData<Exception, int, string, LogLevel> Mapping => new()
    {
        { new KeyNotFoundException("Todo 0192 not found"), 404, "todo.not_found", LogLevel.Information },
        { new InvalidTodoTransitionException(TodoState.Done, TodoState.Scheduled), 409, "todo.invalid_transition", LogLevel.Information },
        { new DbUpdateConcurrencyException("xmin mismatch"), 409, "todo.version_conflict", LogLevel.Warning },
        { new ArgumentException("A scheduled item needs a date.", "scheduledFor"), 400, "validation_failed", LogLevel.Information },
        { new DbUpdateException("other db failure"), 500, "server_error", LogLevel.Error },
        { new InvalidOperationException("secret internal detail"), 500, "server_error", LogLevel.Error },
    };

    [Theory, MemberData(nameof(Mapping))]
    public async Task Maps_exception_to_status_code_and_level(Exception ex, int status, string code, LogLevel level)
    {
        var (handler, http, logs) = Create();

        (await handler.TryHandleAsync(http, ex, CancellationToken.None)).ShouldBeTrue();

        http.Response.StatusCode.ShouldBe(status);
        var body = await ReadBody(http);
        body.GetProperty("code").GetString().ShouldBe(code);
        body.TryGetProperty("traceId", out _).ShouldBeFalse();          // ADR-0008 §3.2

        logs.Count.ShouldBe(1);                                          // the single ILogger call
        logs.LatestRecord.Level.ShouldBe(level);
    }

    [Fact]
    public async Task Invalid_transition_shows_the_domain_message()
    {
        var (handler, http, _) = Create();
        var ex = new InvalidTodoTransitionException(TodoState.Done, TodoState.Scheduled);

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        // The domain authors this message, so it is the one exception whose message reaches the client (ADR-0008 §3.5)
        (await ReadBody(http)).GetProperty("detail").GetString().ShouldBe(ex.Message);
    }

    [Theory]
    [InlineData(typeof(KeyNotFoundException), "Todo 0192 not found")]
    [InlineData(typeof(InvalidOperationException), "secret internal detail")]
    public async Task Never_exposes_framework_exception_messages(Type type, string message)
    {
        var (handler, http, _) = Create();
        await handler.TryHandleAsync(http, (Exception)Activator.CreateInstance(type, message)!, CancellationToken.None);

        (await ReadRaw(http)).ShouldNotContain(message);                 // NFR-3
    }

    [Fact]
    public async Task Validation_errors_are_keyed_by_field_and_strip_the_actual_value()
    {
        var (handler, http, _) = Create();
        var ex = new ArgumentOutOfRangeException("latitude", 95.5, "Latitude must be between -90 and 90.");

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        var errors = (await ReadBody(http)).GetProperty("errors");
        errors.GetProperty("latitude")[0].GetString().ShouldBe("Latitude must be between -90 and 90.");
        (await ReadRaw(http)).ShouldNotContain("95.5");                  // user data never echoed
    }

    [Fact]
    public async Task Client_abort_writes_nothing_and_logs_at_debug()
    {
        var (handler, http, logs) = Create();
        using var cts = new CancellationTokenSource();
        http.RequestAborted = cts.Token;
        cts.Cancel();

        (await handler.TryHandleAsync(http, new OperationCanceledException(), CancellationToken.None)).ShouldBeTrue();

        http.Response.Body.Length.ShouldBe(0);
        logs.LatestRecord.Level.ShouldBe(LogLevel.Debug);
    }

    private static (TodoExceptionHandler, DefaultHttpContext, FakeLogCollector) Create()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails(o => o.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions.Remove("traceId"))
            .BuildServiceProvider();

        var logger = new FakeLogger<TodoExceptionHandler>();
        var handler = new TodoExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), logger);
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        return (handler, http, logger.Collector);
    }

    private static async Task<string> ReadRaw(HttpContext http)
    {
        http.Response.Body.Position = 0;
        return await new StreamReader(http.Response.Body).ReadToEndAsync();
    }

    private static async Task<JsonElement> ReadBody(HttpContext http) =>
        JsonDocument.Parse(await ReadRaw(http)).RootElement;
}