using Todo.Domain.Exceptions;

namespace Todo.Domain.Entities;

// Zero framework dependencies — no EF Core, no ASP.NET types here.
// State-transition METHODS are deliberately left as TODOs: writing
// TodoStateMachineTests first (encoding every arrow in design.md's state diagram), then
// implement against those tests — not have this scaffold pre-decide the logic.
public class TodoItem
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = default!;
    public TodoState State { get; private set; }
    public string LocationAddress { get; private set; } = default!;
    public double? LocationLatitude { get; private set; }
    public double? LocationLongitude { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TodoItem() { } // EF Core materialization

    public static TodoItem Create(
        Guid ownerId, string title, string locationAddress,
        double? lat, double? lng, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(locationAddress))
            throw new ArgumentException("Location address is required.", nameof(locationAddress));

        var now = clock.UtcNow;
        return new TodoItem
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = title,
            State = TodoState.Todo,
            LocationAddress = locationAddress,
            LocationLatitude = lat,
            LocationLongitude = lng,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // Anything not listed here (including
    // a same-state "transition", and anything leaving Done) is rejected. Checked BEFORE the
    // scheduledFor requirement below, so an illegal transition always reports as illegal,
    // even if scheduledFor is also missing.
    private static readonly HashSet<(TodoState From, TodoState To)> LegalTransitions = new()
    {
        (TodoState.Todo, TodoState.Scheduled),
        (TodoState.Scheduled, TodoState.Todo),
        (TodoState.Todo, TodoState.Done),
        (TodoState.Scheduled, TodoState.Done),
    };

    public void TransitionTo(TodoState target, DateTimeOffset? scheduledFor, IClock clock)
    {
        if (!LegalTransitions.Contains((State, target)))
            throw new InvalidTodoTransitionException(State, target);

        if (target == TodoState.Scheduled && scheduledFor is null)
            throw new ArgumentException("scheduledFor is required when scheduling a todo.", nameof(scheduledFor));

        // Unscheduling (Scheduled -> Todo) clears scheduledFor (design.md's invariant).
        // Moving to Done deliberately leaves scheduledFor untouched — it's kept as a record
        // of when the job was originally scheduled for, not cleared.
        ScheduledFor = target == TodoState.Todo ? null : (target == TodoState.Scheduled ? scheduledFor : ScheduledFor);
        State = target;
        UpdatedAt = clock.UtcNow;
    }

    public void UpdateDetails(string title, string locationAddress, double? lat, double? lng, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        Title = title;
        LocationAddress = locationAddress;
        LocationLatitude = lat;
        LocationLongitude = lng;
        UpdatedAt = clock.UtcNow;
    }
}
