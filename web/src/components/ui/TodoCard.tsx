"use client";

import { useContext, useState } from "react";
import { useDeleteTodo, useTransitionTodo, useUpdateTodo } from "../../lib/use-todos";
import { ConcurrencyConflictError, type TodoDto } from "../../lib/todos-api";
import { DeleteModalContext } from "./contexts/DeleteModalContext";

const stateStyles: Record<string, { border: string; dot: string }> = {
  todo: { border: "border-l-moss-600", dot: "bg-moss-600" },
  scheduled: { border: "border-l-gold-600", dot: "bg-gold-600" },
  done: { border: "border-l-sage-600", dot: "bg-sage-600" },
};

export function TodoCard({ todo }: { todo: TodoDto }) {
  const [scheduleDate, setScheduleDate] = useState("");
  const [isEditing, setIsEditing] = useState(false);
  const [title, setTitle] = useState(todo.title);
  const [address, setAddress] = useState(todo.location.address);

  const transition = useTransitionTodo();
  const update = useUpdateTodo();
  //const del = useDeleteTodo();
  const style = stateStyles[todo.state] ?? stateStyles.todo;

  const requestDelete = useContext(DeleteModalContext);

  function handleSave() {
    update.mutate(
      { id: todo.id, title, location: { address, latitude: null, longitude: null }, eTag: todo.eTag },
      { onSuccess: () => setIsEditing(false) }
    );
  }

  if (isEditing) {
    return (
      <li className={`rounded-md border border-ink/10 border-l-4 ${style.border} bg-surface p-4 shadow-sm`}>
        <div className="space-y-2">
          <input
            aria-label="Edit title"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            className="block w-full rounded-md border border-ink/15 px-3 py-2"
          />
          <input
            aria-label="Edit location"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            className="block w-full rounded-md border border-ink/15 px-3 py-2"
          />
          <div className="flex gap-2">
            <button
              onClick={handleSave}
              disabled={update.isPending}
              className="h-9 rounded-md bg-moss-600 px-3 text-sm font-medium text-white hover:bg-moss-700 disabled:opacity-50"
            >
              Save
            </button>
            <button
              onClick={() => setIsEditing(false)}
              className="h-9 rounded-md border border-ink/20 px-3 text-sm text-ink/70 hover:bg-ink/5"
            >
              Cancel
            </button>
          </div>
          {update.isError && (
            <p role="alert" className="text-sm text-danger">
              {update.error instanceof ConcurrencyConflictError
                ? update.error.message
                : `Couldn&apos;t save: ${update.error.message}`}
            </p>
          )}
        </div>
      </li>
    );
  }

  return (
    <li className={`rounded-md border border-ink/10 border-l-4 ${style.border} bg-surface p-4 shadow-sm`}>
      <div className="flex items-start justify-between gap-3">
        <h3 className="font-heading font-medium text-ink">{todo.title}</h3>
        <span className="flex items-center gap-1.5 text-xs text-ink/60">
          <span className={`h-2 w-2 rounded-full ${style.dot}`} />
          {todo.state}
        </span>
      </div>
      <p className="mt-1 text-sm text-ink/60">{todo.location.address}</p>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        {todo.state === "todo" && (
          <>
            <input
              type="date"
              aria-label="Schedule date"
              value={scheduleDate}
              onChange={(e) => setScheduleDate(e.target.value)}
              className="rounded-md border border-ink/15 px-2 py-1.5 text-sm"
            />
            <button
              disabled={!scheduleDate || transition.isPending}
              onClick={() => transition.mutate({ id: todo.id, targetState: "scheduled", scheduledFor: scheduleDate })}
              className="h-9 rounded-md border border-moss-600 px-3 text-sm font-medium text-moss-700 hover:bg-moss-600/10 disabled:opacity-50"
            >
              Schedule
            </button>
          </>
        )}
        {todo.state === "scheduled" && (
          <button
            disabled={transition.isPending}
            onClick={() => transition.mutate({ id: todo.id, targetState: "todo" })}
            className="h-9 rounded-md border border-ink/20 px-3 text-sm font-medium text-ink/70 hover:bg-ink/5 disabled:opacity-50"
          >
            Unschedule
          </button>
        )}
        {todo.state !== "done" && (
          <button
            disabled={transition.isPending}
            onClick={() => transition.mutate({ id: todo.id, targetState: "done" })}
            className="h-9 rounded-md bg-moss-600 px-3 text-sm font-medium text-white hover:bg-moss-700 disabled:opacity-50"
          >
            Mark done
          </button>
        )}
        <button
          onClick={() => setIsEditing(true)}
          className="h-9 rounded-md border border-ink/20 px-3 text-sm text-ink/70 hover:bg-ink/5"
        >
          Edit
        </button>
        <button
          onClick={() => requestDelete(todo.id)}
          className="h-9 rounded-md border border-danger/40 px-3 text-sm text-danger hover:bg-danger/5 disabled:opacity-50"
        >
          Delete
        </button>
      </div>

      {transition.isError && (
        <p role="alert" className="mt-2 text-sm text-danger">{transition.error.message}</p>
      )}
    </li>
  );
}
