using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Todo.Api.Extensions;
using Todo.Application.Todos;
using Todo.Application.Todos.Dtos;
using Todo.Domain.Entities;

namespace Todo.Api.Controllers;

[ApiController]
[Authorize] // every action below now genuinely requires a valid session
[Route("todos")] // ingress strips /api before forwarding — see ADR-0002's rewrite note
public class TodosController : ControllerBase
{
    private readonly TodoService _todos;

    public TodosController(TodoService todos) => _todos = todos;

    // Built against a hardcoded OwnerId deliberately, so those tests stayed
    // simple; this retrofits real ownership onto already-working, already-tested behaviour
    // by reading it from the authenticated JWT instead.
    private Guid OwnerId => User.GetOwnerId();

    [HttpPost]
    public async Task<ActionResult<TodoDto>> Create(
        [FromBody] CreateTodoRequest request, CancellationToken ct)
    {
        var dto = await _todos.CreateAsync(OwnerId, request, ct);
        return CreatedAtAction(nameof(Create), new { id = dto.Id }, dto);
    }

    [HttpGet]
    public async Task<ActionResult<TodoPageDto>> List(
        [FromQuery] TodoState? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return ValidationProblem("page must be >=1 and pageSize must be 1-100.");

        var result = await _todos.ListAsync(OwnerId, state, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TodoDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await _todos.GetByIdAsync(OwnerId, id, ct);
        return Ok(dto);
    }

    [HttpPost("{id:guid}/transition")]
    public async Task<ActionResult<TodoDto>> Transition(
        Guid id, [FromBody] TransitionTodoRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<TodoState>(request.TargetState, ignoreCase: true, out var target))
            return ValidationProblem($"Unknown target state '{request.TargetState}'.");

        var dto = await _todos.TransitionAsync(OwnerId, id, target, request.ScheduledFor, ct);
        return Ok(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TodoDto>> Update(
        Guid id, [FromBody] UpdateTodoRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
            return ValidationProblem("If-Match header is required.");

        var dto = await _todos.UpdateAsync(OwnerId, id, request, ifMatch, ct);
        return Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _todos.DeleteAsync(OwnerId, id, ct);
        return NoContent();
    }
}