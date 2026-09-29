import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createTodo, deleteTodo, fetchTodos, transitionTodo, updateTodo,
  type CreateTodoRequest,
} from "./todos-api";

export function useTodos(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: ["todos", page, pageSize],
    queryFn: () => fetchTodos(page, pageSize),
  });
}

export function useCreateTodo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateTodoRequest) => createTodo(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["todos"] }),
  });
}

export function useTransitionTodo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, targetState, scheduledFor }: {
      id: string; targetState: "todo" | "scheduled" | "done"; scheduledFor?: string;
    }) => transitionTodo(id, targetState, scheduledFor),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["todos"] }),
  });
}

export function useUpdateTodo() {
  const queryClient = useQueryClient();
  return useMutation({
    // NOT auto-retried on failure by default (TanStack Query's mutation default) — correct
    // here specifically because a 409 must surface to the user, never be silently retried
    // (ADR-0023: a concurrency conflict is an application decision, not a transient fault).
    mutationFn: (args: {
      id: string; title: string;
      location: { address: string; latitude: number | null; longitude: number | null };
      eTag: string;
    }) => updateTodo(args.id, args.title, args.location, args.eTag),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["todos"] }),
  });
}

export function useDeleteTodo() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteTodo(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["todos"] }),
  });
}
