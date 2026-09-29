using Todo.Domain;
using Todo.Domain.Entities;
using Todo.Domain.Exceptions;
using Xunit;

namespace Todo.UnitTests;

// Fast, isolated — no HTTP host, no database. Every case here maps directly
// to an arrow (or a deliberately absent arrow) in design.md's state diagram.
public class TodoStateMachineTests
{
    private static readonly IClock FixedClock = new FixedClockStub(DateTimeOffset.Parse("2026-09-27T09:00:00Z"));

    private static TodoItem NewTodo() =>
        TodoItem.Create(Guid.NewGuid(), "Trim hedges", "42 Example St", null, null, FixedClock);

    [Fact]
    public void Todo_to_scheduled_requires_and_stores_scheduledFor()
    {
        var todo = NewTodo();
        var scheduledFor = DateTimeOffset.Parse("2026-10-01T09:00:00Z");

        todo.TransitionTo(TodoState.Scheduled, scheduledFor, FixedClock);

        Assert.Equal(TodoState.Scheduled, todo.State);
        Assert.Equal(scheduledFor, todo.ScheduledFor);
    }

    [Fact]
    public void Todo_to_scheduled_without_scheduledFor_throws()
    {
        var todo = NewTodo();

        Assert.Throws<ArgumentException>(() =>
            todo.TransitionTo(TodoState.Scheduled, scheduledFor: null, FixedClock));
    }

    [Fact]
    public void Scheduled_to_todo_clears_scheduledFor()
    {
        var todo = NewTodo();
        todo.TransitionTo(TodoState.Scheduled, DateTimeOffset.Parse("2026-10-01T09:00:00Z"), FixedClock);

        todo.TransitionTo(TodoState.Todo, scheduledFor: null, FixedClock);

        Assert.Equal(TodoState.Todo, todo.State);
        Assert.Null(todo.ScheduledFor);
    }

    [Fact]
    public void Scheduled_to_done_succeeds()
    {
        var todo = NewTodo();
        todo.TransitionTo(TodoState.Scheduled, DateTimeOffset.Parse("2026-10-01T09:00:00Z"), FixedClock);

        todo.TransitionTo(TodoState.Done, scheduledFor: null, FixedClock);

        Assert.Equal(TodoState.Done, todo.State);
    }

    [Fact]
    public void Todo_to_done_directly_succeeds()
    {
        var todo = NewTodo();

        todo.TransitionTo(TodoState.Done, scheduledFor: null, FixedClock);

        Assert.Equal(TodoState.Done, todo.State);
    }

    [Theory]
    [InlineData(TodoState.Todo)]
    [InlineData(TodoState.Scheduled)]
    [InlineData(TodoState.Done)]
    public void Done_is_terminal_no_transition_leaves_it(TodoState attemptedTarget)
    {
        var todo = NewTodo();
        todo.TransitionTo(TodoState.Done, scheduledFor: null, FixedClock);

        var ex = Assert.Throws<InvalidTodoTransitionException>(() =>
            todo.TransitionTo(attemptedTarget, scheduledFor: null, FixedClock));

        Assert.Equal(TodoState.Done, ex.CurrentState);
    }

    [Fact]
    public void Transitioning_to_the_same_state_is_treated_as_illegal()
    {
        // Explicit design decision, not an oversight: we've listed real transitions between
        // distinct states; a same-state "transition" isn't one of them, so it's rejected
        // rather than silently accepted as a no-op.
        var todo = NewTodo();

        Assert.Throws<InvalidTodoTransitionException>(() =>
            todo.TransitionTo(TodoState.Todo, scheduledFor: null, FixedClock));
    }
}

internal class FixedClockStub : IClock
{
    public FixedClockStub(DateTimeOffset now) => UtcNow = now;
    public DateTimeOffset UtcNow { get; }
}
