import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { TodoForm } from "./TodoForm";

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe("TodoForm", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("submits title and location, then clears the form on success", async () => {
    (fetch as ReturnType<typeof vi.fn>)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ id: "1", title: "Trim hedges" }),
      })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [] }) });

    renderWithClient(<TodoForm />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Title"), "Trim hedges");
    await user.type(screen.getByLabelText("Location"), "42 Example St");
    await user.click(screen.getByRole("button", { name: /add job/i }));

    await waitFor(() => expect(screen.getByLabelText("Title")).toHaveValue(""));
    expect(fetch).toHaveBeenCalledWith(
      "/api/todos",
      expect.objectContaining({ method: "POST" })
    );
  });

  it("shows an error message when the API call fails", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: false,
      status: 400,
      json: async () => ({ detail: "Title is required." }),
    });

    renderWithClient(<TodoForm />);
    const user = userEvent.setup();

    await user.type(screen.getByLabelText("Title"), "x");
    await user.type(screen.getByLabelText("Location"), "y");
    await user.click(screen.getByRole("button", { name: /add job/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Title is required.");
  });
});
