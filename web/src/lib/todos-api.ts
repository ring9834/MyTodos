import type { paths } from "./api-types";

type TodoDto = paths["/todos"]["get"]["responses"][200]["content"]["application/json"]["items"][number];
type TodoPageDto = paths["/todos"]["get"]["responses"][200]["content"]["application/json"];
type CreateTodoRequest = paths["/todos"]["post"]["requestBody"]["content"]["application/json"];

export async function fetchTodos(page = 1, pageSize = 20): Promise<TodoPageDto> {
  const res = await fetch(`/api/todos?page=${page}&pageSize=${pageSize}`, {
    credentials: "same-origin",
  });
  if (!res.ok) throw new Error(`Failed to load todos: ${res.status}`);
  return res.json();
}

export async function createTodo(request: CreateTodoRequest): Promise<TodoDto> {
  const res = await fetch("/api/todos", {
    credentials: "same-origin",
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Failed to create todo: ${res.status}`);
  }
  return res.json();
}

export async function transitionTodo(
  id: string,
  targetState: "todo" | "scheduled" | "done",
  scheduledFor?: string
): Promise<TodoDto> {
  const res = await fetch(`/api/todos/${id}/transition`, {
    credentials: "same-origin",
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ targetState, scheduledFor }),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Failed to update: ${res.status}`);
  }
  return res.json();
}

// Distinct error type so the UI can tell "you're editing stale data" apart from any
// other failure (design.md's "this todo changed elsewhere, reload?" — Q6).
export class ConcurrencyConflictError extends Error {
  constructor() { super("This job changed elsewhere. Reload to see the latest version."); }
}

export async function updateTodo(
  id: string,
  title: string,
  location: { address: string; latitude: number | null; longitude: number | null },
  eTag: string
): Promise<TodoDto> {
  const res = await fetch(`/api/todos/${id}`, {
    credentials: "same-origin",
    method: "PUT",
    headers: { "Content-Type": "application/json", "If-Match": `"${eTag}"` },
    body: JSON.stringify({ title, location }),
  });
  if (res.status === 409) throw new ConcurrencyConflictError();
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Failed to update: ${res.status}`);
  }
  return res.json();
}

export async function deleteTodo(id: string): Promise<void> {
  const res = await fetch(`/api/todos/${id}`, { method: "DELETE", credentials: "same-origin" });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Failed to delete: ${res.status}`);
  }
}

export type { TodoDto, TodoPageDto, CreateTodoRequest };

export async function register(username: string, password: string): Promise<void> {
  const res = await fetch("/api/auth/register", {
    credentials: "same-origin",
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Registration failed: ${res.status}`);
  }
}

export async function login(username: string, password: string): Promise<void> {
  const res = await fetch("/api/auth/login", {
    credentials: "same-origin",
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => null);
    throw new Error(problem?.detail ?? `Login failed: ${res.status}`);
  }
}

export async function logout(): Promise<void> {
  await fetch("/api/auth/logout", { method: "POST", credentials: "same-origin" });
}
