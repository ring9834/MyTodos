import { createContext } from "react";

// The "shape" of what we're broadcasting:
// a function that takes an id and does nothing by default.
export const DeleteModalContext = createContext<(id: string) => void>(
  () => {}
);