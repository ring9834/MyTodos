"use client";

import { useState } from "react";
import { useDeleteTodo, useTransitionTodo, useUpdateTodo } from "../../lib/use-todos";

const stateStyles: Record<string, { border: string; dot: string }> = {
  todo: { border: "border-l-moss-600", dot: "bg-moss-600" },
  scheduled: { border: "border-l-gold-600", dot: "bg-gold-600" },
  done: { border: "border-l-sage-600", dot: "bg-sage-600" },
};

export function ModelWindow({ id }: { id: string  }) {
  const [isOpen, setIsOpen] = useState(false);

  function handleSigal(flag: boolean) {
    setIsOpen(flag);
    if (flag) {
      const del = useDeleteTodo();
      del.mutate(id);
      setIsOpen(false);
    }
  }

  if (isOpen) {
    return (
      <li className={`rounded-md border border-ink/10 border-l-4 bg-surface p-4 shadow-sm`}>
        <div className="space-y-2">
          <div className="flex gap-2">
            <button
              onClick={ () => handleSigal(true)}
              className="h-9 rounded-md bg-moss-600 px-3 text-sm font-medium text-white hover:bg-moss-700 disabled:opacity-50"
            >
              Save
            </button>
            <button
              onClick={() => handleSigal(false)}
              className="h-9 rounded-md border border-ink/20 px-3 text-sm text-ink/70 hover:bg-ink/5"
            >
              Cancel
            </button>
          </div>
        </div>
      </li>
    );
  };
}
