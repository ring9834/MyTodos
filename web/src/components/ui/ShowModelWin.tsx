"use client";

import { useEffect, useState } from "react";
import { createPortal } from "react-dom";

export function ModelWindow({
  show,
  onConfirm,
  onCancel,
}: {
  id: string;
  show: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const [mounted, setMounted] = useState(false);

  // document only exists in the browser, so wait until mounted to portal
  useEffect(() => setMounted(true), []);

  // Close on Escape and lock page scroll while open
  useEffect(() => {
    if (!show) return;
    
    // Reads the current inline overflow value on <body> and stores it in a variable.
    // Then sets the overflow to "hidden" to prevent scrolling while the modal is open.
    // Behave correctly if multiple modals are stacked.
    // When the modal is closed, it restores the original overflow value to allow scrolling again.
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
    };
    window.addEventListener("keydown", handleKey);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", handleKey);
    };
  }, [show, onCancel]);

  if (!mounted || !show) return null;

  return createPortal(
    // Half-transparent backdrop covering the whole page; click it to cancel
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      onClick={onCancel}
    >
      {/* Opaque modal box; clicks inside don't reach the backdrop */}
      <div
        role="dialog"
        aria-modal="true"
        className="w-full max-w-sm h-[100px] sm:h-[300px] rounded-md border border-ink/10 border-l-4 bg-surface p-4 shadow-sm flex items-center flex-col justify-end"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="space-y-2">
          <div className="flex gap-2">
            <button
              type="button"
              onClick={onConfirm}
              autoFocus
              className="h-9 rounded-md bg-moss-600 px-3 text-sm font-medium text-white hover:bg-moss-700 disabled:opacity-50"
            >
              OK
            </button>
            <button
              type="button"
              onClick={onCancel}
              className="h-9 rounded-md border border-ink/20 px-3 text-sm text-ink/70 hover:bg-ink/5"
            >
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body
  );
}
