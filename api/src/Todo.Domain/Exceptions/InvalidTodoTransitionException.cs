using Todo.Domain.Entities;

namespace Todo.Domain.Exceptions;

// Thrown by TodoItem when an illegal state transition is attempted.
// The Api layer maps this to a 409 ProblemDetails response — see openapi.yaml.
public sealed class InvalidTodoTransitionException : Exception
{
    public TodoState CurrentState { get; }
    public TodoState AttemptedTargetState { get; }

    public InvalidTodoTransitionException(TodoState currentState, TodoState attemptedTargetState)
        : base($"Cannot transition from '{currentState}' to '{attemptedTargetState}'.")
    {
        CurrentState = currentState;
        AttemptedTargetState = attemptedTargetState;
    }
}
