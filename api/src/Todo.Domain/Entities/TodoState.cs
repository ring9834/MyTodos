namespace Todo.Domain.Entities;

// Legal transitions are todo->scheduled->done, scheduled->todo, todo->done.
// No transition leaves "done" (terminal). Enforced in TodoItem, not here.
public enum TodoState
{
    Todo,
    Scheduled,
    Done
}
