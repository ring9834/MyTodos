using Microsoft.EntityFrameworkCore;
using Todo.Application.Todos.Dtos;
using Todo.Domain;
using Todo.Domain.Entities;
using Todo.Infrastructure;

namespace Todo.Application.Todos;

public class TodoService
{
    private readonly TodoDbContext _db;
    private readonly IClock _clock;

    public TodoService(TodoDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<TodoDto> CreateAsync(Guid ownerId, CreateTodoRequest request, CancellationToken ct)
    {
        var todo = TodoItem.Create(
            ownerId, request.Title, request.Location.Address,
            request.Location.Latitude, request.Location.Longitude, _clock);

        _db.Todos.Add(todo);
        await _db.SaveChangesAsync(ct);
        return ToDto(todo);
    }

    public async Task<TodoPageDto> ListAsync(
        Guid ownerId, TodoState? state, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Todos.Where(t => t.OwnerId == ownerId);
        if (state is not null)
            query = query.Where(t => t.State == state);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new TodoPageDto(items.Select(ToDto).ToList(), page, pageSize, totalCount);
    }

    public async Task<TodoDto> GetByIdAsync(Guid ownerId, Guid todoId, CancellationToken ct)
    {
        var todo = await _db.Todos.FirstOrDefaultAsync(t => t.Id == todoId && t.OwnerId == ownerId, ct);
        if (todo is null)
            throw new KeyNotFoundException(); // Q1: not found or not owned -> 404, never leak which

        return ToDto(todo);
    }

    public async Task<TodoDto> TransitionAsync(
        Guid ownerId, Guid todoId, TodoState target, DateTimeOffset? scheduledFor, CancellationToken ct)
    {
        var todo = await _db.Todos.FirstOrDefaultAsync(t => t.Id == todoId && t.OwnerId == ownerId, ct);
        if (todo is null)
            throw new KeyNotFoundException();

        todo.TransitionTo(target, scheduledFor, _clock);
        await _db.SaveChangesAsync(ct);
        return ToDto(todo);
    }

    // If-Match's value is the eTag the client last saw, sourced from the list
    // body (not a header, per the openapi.yaml gap this slice fixed). We set it as the
    // shadow xmin property's ORIGINAL value, so EF Core's own optimistic-concurrency check
    // (comparing that original against the row's *actual* current xmin at UPDATE time)
    // throws DbUpdateConcurrencyException — already mapped to 409 (TodoExceptionHandler) —
    // if someone else changed the row in between. this is the most advanced EF Core pattern in the project so far.
    public async Task<TodoDto> UpdateAsync(
        Guid ownerId, Guid todoId, UpdateTodoRequest request, string ifMatch, CancellationToken ct)
    {
        var todo = await _db.Todos.FirstOrDefaultAsync(t => t.Id == todoId && t.OwnerId == ownerId, ct);
        if (todo is null)
            throw new KeyNotFoundException();

        if (!uint.TryParse(ifMatch.Trim('"'), out var expectedXmin))
            throw new ArgumentException("If-Match is not a valid concurrency token.", nameof(ifMatch));

        _db.Entry(todo).Property<uint>("xmin").OriginalValue = expectedXmin;

        todo.UpdateDetails(request.Title, request.Location.Address,
            request.Location.Latitude, request.Location.Longitude, _clock);

        await _db.SaveChangesAsync(ct); // throws DbUpdateConcurrencyException on a stale If-Match
        return ToDto(todo);
    }

    public async Task DeleteAsync(Guid ownerId, Guid todoId, CancellationToken ct)
    {
        var todo = await _db.Todos.FirstOrDefaultAsync(t => t.Id == todoId && t.OwnerId == ownerId, ct);
        if (todo is null)
            throw new KeyNotFoundException(); // DELETE is idempotent (ADR-0023) — this 404
                                                // is the harmless "already gone" case too

        _db.Todos.Remove(todo);
        await _db.SaveChangesAsync(ct);
    }

    private TodoDto ToDto(TodoItem t)
    {
        var xmin = _db.Entry(t).Property<uint>("xmin").CurrentValue;
        return new TodoDto(
            t.Id, t.Title, t.State.ToString().ToLowerInvariant(),
            new LocationDto(t.LocationAddress, t.LocationLatitude, t.LocationLongitude),
            t.ScheduledFor, t.CreatedAt, t.UpdatedAt,
            ETag: xmin.ToString());
    }
}