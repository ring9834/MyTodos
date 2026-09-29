namespace Todo.Domain;

// Narrow port: the ONE deliberate exception to "no repository/port layer".
// Exists purely so ScheduledFor / overdue logic is testable without waiting on the real clock.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
