"use client";

import { useState } from "react";
import { useCreateTodo } from "../../lib/use-todos";

export function TodoForm() {
  const [title, setTitle] = useState("");
  const [address, setAddress] = useState("");
  const createTodo = useCreateTodo();

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    createTodo.mutate(
      { title, location: { address, latitude: null, longitude: null } },
      { onSuccess: () => { setTitle(""); setAddress(""); } }
    );
  }

  return (
    <form
      onSubmit={handleSubmit}
      aria-label="Add a job"
      className="rounded-lg border border-moss-900/10 bg-surface p-5 shadow-sm sm:p-6"
    >
      <h2 className="font-heading text-lg font-semibold text-ink">Add a job</h2>

      <div className="mt-4 space-y-4">
        <div>
          <label htmlFor="title" className="block text-sm font-medium text-ink/80">
            Title
          </label>
          <input
            id="title"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            required
            placeholder="Trim the front hedges"
            maxLength={200}
            className="mt-1 block w-full rounded-md border border-ink/15 bg-white px-3 py-2.5 text-ink placeholder:text-ink/40 focus:border-moss-600 focus:outline-none focus:ring-2 focus:ring-moss-600/40"
          />
        </div>

        <div>
          <label htmlFor="address" className="block text-sm font-medium text-ink/80">
            Location
          </label>
          <input
            id="address"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            required
            placeholder="42 Example St"
            maxLength={500}
            className="mt-1 block w-full rounded-md border border-ink/15 bg-white px-3 py-2.5 text-ink placeholder:text-ink/40 focus:border-moss-600 focus:outline-none focus:ring-2 focus:ring-moss-600/40"
          />
        </div>

        <button
          type="submit"
          disabled={createTodo.isPending}
          className="inline-flex h-11 w-full items-center justify-center rounded-md bg-moss-600 px-4 font-medium text-white transition-colors hover:bg-moss-700 focus:outline-none focus:ring-2 focus:ring-moss-600/50 focus:ring-offset-2 disabled:opacity-60 sm:w-auto"
        >
          {createTodo.isPending ? "Adding…" : "Add job"}
        </button>

        {createTodo.isError && (
          <p role="alert" className="text-sm text-danger">
            {createTodo.error.message}
          </p>
        )}
      </div>
    </form>
  );
}
