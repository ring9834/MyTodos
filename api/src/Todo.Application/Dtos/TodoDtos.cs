namespace Todo.Application.Todos.Dtos;

// DTOs only cross the Api boundary — controllers never return EF entities directly
// (AGENTS.md rule). Shapes match docs/openapi.yaml.
public record LocationDto(string Address, double? Latitude, double? Longitude);

public record TodoDto(
    Guid Id, string Title, string State, LocationDto Location,
    DateTimeOffset? ScheduledFor, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string ETag);

public record UpdateTodoRequest(string Title, LocationDto Location);

public record TodoPageDto(IReadOnlyList<TodoDto> Items, int Page, int PageSize, int TotalCount);

public record CreateTodoRequest(string Title, LocationDto Location);

public record TransitionTodoRequest(string TargetState, DateTimeOffset? ScheduledFor);
