"use client";

import { useState } from "react";
import { login, logout, register } from "../../lib/todos-api";

export function AuthGate({ children }: { children: React.ReactNode }) {
  const [username, setUsernameState] = useState<string | null>(null);
  const [mode, setMode] = useState<"login" | "register">("login");
  const [formUsername, setFormUsername] = useState("");
  const [formPassword, setFormPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleLogout() {
    await logout();
    setUsernameState(null);
  }

  // Known simplification: login state is client-side only, so a page refresh loses it even
  // though the session cookie itself persists server-side until logout/expiry. A `/auth/me`
  // check on mount would close this gap — UX polish, not a security one, since the server
  // still fully enforces [Authorize] regardless of what this component believes.
  if (username) {
    return (
      <div>
        <div className="flex items-center justify-between border-b border-ink/10 bg-surface px-4 py-2 sm:px-6">
          <span className="text-sm text-ink/60">Logged in as <strong className="text-ink">{username}</strong></span>
          <button onClick={handleLogout} className="text-sm text-ink/60 underline hover:text-ink">
            Log out
          </button>
        </div>
        {children}
      </div>
    );
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setIsSubmitting(true);
    try {
      if (mode === "register") await register(formUsername, formPassword);
      else await login(formUsername, formPassword);
      setUsernameState(formUsername);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Something went wrong.");
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <main className="mx-auto flex min-h-screen max-w-sm flex-col justify-center px-4">
      <form onSubmit={handleSubmit} className="rounded-lg border border-moss-900/10 bg-surface p-6 shadow-sm">
        <h1 className="font-heading text-xl font-semibold text-ink">
          {mode === "login" ? "Log in" : "Create an account"}
        </h1>

        <div className="mt-4 space-y-3">
          <div>
            <label htmlFor="username" className="block text-sm font-medium text-ink/80">Username</label>
            <input
              id="username"
              value={formUsername}
              onChange={(e) => setFormUsername(e.target.value)}
              required
              minLength={mode === "register" ? 3 : undefined}
              className="mt-1 block w-full rounded-md border border-ink/15 px-3 py-2"
            />
            {mode === "register" && (
              <p className="mt-1 text-xs text-ink/50">At least 3 characters.</p>
            )}
          </div>
          <div>
            <label htmlFor="password" className="block text-sm font-medium text-ink/80">Password</label>
            <input
              id="password"
              type="password"
              value={formPassword}
              onChange={(e) => setFormPassword(e.target.value)}
              required
              minLength={mode === "register" ? 8 : undefined}
              className="mt-1 block w-full rounded-md border border-ink/15 px-3 py-2"
            />
            {mode === "register" && (
              <p className="mt-1 text-xs text-ink/50">At least 8 characters.</p>
            )}
          </div>

          <button
            type="submit"
            disabled={isSubmitting}
            className="h-11 w-full rounded-md bg-moss-600 font-medium text-white hover:bg-moss-700 disabled:opacity-60"
          >
            {mode === "login" ? "Log in" : "Register"}
          </button>

          {error && <p role="alert" className="text-sm text-danger">{error}</p>}

          <button
            type="button"
            onClick={() => { setMode(mode === "login" ? "register" : "login"); setError(null); }}
            className="text-sm text-ink/60 underline"
          >
            {mode === "login" ? "Need an account? Register" : "Already have an account? Log in"}
          </button>
        </div>
      </form>
    </main>
  );
}
