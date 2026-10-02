"use client";

import { useState } from "react";
import { AuthGate } from "../components/auth/AuthGate";
import { TodoForm } from "../components/ui/TodoForm";
import { TodoList } from "../components/ui/TodoList";
import { DeleteModalContext } from "../components/ui/contexts/DeleteModalContext";
import { ModelWindow } from "../components/ui/ShowModelWin";
import { useDeleteTodo } from "../lib/use-todos";

export default function Home() {
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const requestDelete = (id: string) => {
    setPendingDeleteId(id);
  };

  const handleConfirmDelete = () => {
    // Perform the delete action here using the pendingDeleteId
    console.log("Deleting todo with id:", pendingDeleteId);
    del.mutate(pendingDeleteId!);

    // After deletion, reset the pendingDeleteId
    setPendingDeleteId(null);
  };

  const handleCancelDelete = () => {
    setPendingDeleteId(null);
  };

  const del = useDeleteTodo();

  return (
    <AuthGate>
      <main className="mx-auto max-w-5xl px-4 py-8 sm:px-6 sm:py-12">
        <header className="mb-8">
          <h1 className="font-heading text-3xl font-bold text-ink sm:text-4xl">
            {"Today's jobs"}
          </h1>
          <p className="mt-1 text-ink/60">
            Add a job below and check it off as you go.
          </p>
        </header>

        <div className="grid grid-cols-1 gap-6 md:grid-cols-[minmax(260px,320px)_1fr] md:items-start">
          <div className="md:sticky md:top-8">
            <TodoForm />
          </div>
          <DeleteModalContext.Provider value={requestDelete}>
            <TodoList />
          </DeleteModalContext.Provider>
        </div>

        <ModelWindow id={pendingDeleteId ?? ""} show={!!pendingDeleteId} onConfirm={handleConfirmDelete} onCancel={handleCancelDelete} />
      </main>
    </AuthGate>
  );
}
