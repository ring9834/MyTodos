import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { TodoCard } from "./TodoCard";
import type { TodoDto } from "../../lib/todos-api";

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const baseTodo: TodoDto = {
  id: "1", title: "Mow the lawn", state: "todo",
  location: { address: "1 Example Rd", latitude: null, longitude: null },
  scheduledFor: null, createdAt: "2026-09-27T00:00:00Z", updatedAt: "2026-09-27T00:00:00Z",
  eTag: "42",
};

describe("TodoCard", () => {
  beforeEach(() => { vi.stubGlobal("fetch", vi.fn()); });

  it("shows Schedule and Mark done for a todo-state item, and calls the transition endpoint", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true, json: async () => ({ ...baseTodo, state: "done" }),
    });
    renderWithClient(<TodoCard todo={baseTodo} />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /mark done/i }));

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(
      "/api/todos/1/transition",
      expect.objectContaining({ method: "POST" })
    ));
  });

  it("disables Schedule until a date is chosen", () => {
    renderWithClient(<TodoCard todo={baseTodo} />);
    expect(screen.getByRole("button", { name: /^schedule$/i })).toBeDisabled();
  });

  it("shows Edit and Delete even for a done-state item, but no Schedule/Unschedule", () => {
    renderWithClient(<TodoCard todo={{ ...baseTodo, state: "done" }} />);
    expect(screen.getByRole("button", { name: /^edit$/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^delete$/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^schedule$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /mark done/i })).not.toBeInTheDocument();
  });

  it("Edit -> Save sends PUT with If-Match set to the todo&apos;s current eTag", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true, json: async () => ({ ...baseTodo, title: "Mow the back lawn" }),
    });
    renderWithClient(<TodoCard todo={baseTodo} />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /^edit$/i }));
    const titleInput = screen.getByLabelText("Edit title");
    await user.clear(titleInput);
    await user.type(titleInput, "Mow the back lawn");
    await user.click(screen.getByRole("button", { name: /^save$/i }));

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(
      "/api/todos/1",
      expect.objectContaining({
        method: "PUT",
        headers: expect.objectContaining({ "If-Match": '"42"' }),
      })
    ));
  });

  it("shows the concurrency-conflict message on a 409, distinct from other errors", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ ok: false, status: 409 });
    renderWithClient(<TodoCard todo={baseTodo} />);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: /^edit$/i }));
    await user.click(screen.getByRole("button", { name: /^save$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/changed elsewhere/i);
  });

  // it("Delete calls the DELETE endpoint", async () => {
  //   (fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ ok: true, status: 204 });
  //   renderWithClient(<TodoCard todo={baseTodo} />);
  //   const user = userEvent.setup();

  //   await user.click(screen.getByRole("button", { name: /^delete$/i }));

  //   await waitFor(() => expect(fetch).toHaveBeenCalledWith(
  //     "/api/todos/1",
  //     expect.objectContaining({ method: "DELETE" })
  //   ));
  // });
});
