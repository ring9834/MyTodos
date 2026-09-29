"use client";

import { useTodos } from "../../lib/use-todos";
import { TodoCard } from "./TodoCard";

export function TodoList() {
  const { data, isLoading, isError, error } = useTodos();

  if (isLoading) return <p className="text-ink/60">Pulling up today&apos;s jobs…</p>;

  if (isError) {
    return (
      <p role="alert" className="rounded-md bg-danger/10 px-4 py-3 text-danger">
        Couldn&apos;t reach the job list. Check your connection and try again.
        <span className="block text-sm text-danger/70">{error.message}</span>
      </p>
    );
  }

  if (!data) return null;

  if (data.items.length === 0) {
    return (
      <p className="rounded-md border border-dashed border-ink/20 px-4 py-6 text-center text-ink/60">
        No jobs on the list yet. Add the first one above.
      </p>
    );
  }

  return (
    <ul className="space-y-3">
      {data.items.map((todo) => (
        <TodoCard key={todo.id} todo={todo} />
      ))}
    </ul>
  );
}
