# AGENTS.md: conventions for AI assistants (and for me)

## Project
Todo / job-planning app. A todo has a title, a state (`todo` | `scheduled` | `done`) and a location.
Built for a time-boxed assessment, but treated as production software.
Read `docs/requirements.md` and `docs/architecture.md` before proposing changes.
Definition of Done = `docs/production-readiness.md`.

## Stack
- API: .NET (current LTS), C#, ASP.NET Core **controllers**, EF Core + Npgsql, PostgreSQL
- Web: Next.js (App Router), React, TypeScript (`strict`)
- Auth: JWT bearer validation; every todo row has an `OwnerId`
- Infra: Terraform, Kubernetes, Helm, GitHub Actions (cloud: Azure unless an ADR says otherwise)

## Repo layout (target)
```
api/        .NET solution (src/, tests/)
web/        Next.js app
infra/      Terraform
deploy/     Helm chart(s)
docs/       requirements, architecture, ADRs, openapi.yaml, checklists
```
API target layering (decided in ADR): `Api` (controllers) -> `Application` -> `Domain`; `Infrastructure` implements ports.
Domain has no framework dependencies.

## How to work with me
- Spec first: acceptance criteria exist before code. Propose a short plan before any change touching more than ~3 files.
- Small increments: one behaviour per PR, one PR per thin vertical slice (UI + API + DB).
- Prefer simple, conventional, small-file code I can rewrite by hand. No clever abstractions, no speculative generality.
- Explain non-obvious choices in a comment or the PR description.

## Rules
**Testing**
- Every behaviour change ships with tests in the same PR. Never disable or weaken a test to get CI green.
- Domain state machine: unit tests. API: integration tests against real PostgreSQL (Testcontainers). Frontend: component tests for forms and loading/empty/error states.

**API**
- Controllers stay thin. DTOs only; never return EF entities.
- Errors are RFC 9457 `ProblemDetails`. List endpoints are paginated.
- Every query is filtered by `OwnerId`. Another user's resource returns 404.
- Optimistic concurrency via ETag / concurrency token (Npgsql `xmin`; PostgreSQL has no `rowversion`).
- Schema changes go through EF Core migrations and must be backward-compatible (expand/contract).

**Frontend**
- No `any`. API types come from `docs/openapi.yaml` (generated client), not hand-written.
- Every data view handles loading, empty and error states. Forms have labels and keyboard support.

**Security**
- Never commit secrets. Config comes from environment variables / Key Vault.
- Never log tokens or personal data.

**Infrastructure**
- Never invent Terraform or Helm arguments. Verify with `terraform validate` / `plan` and `helm lint` / `template`, and show the output.
- Containers run as non-root with resource limits and probes.

**Dependencies**
- Do not add a dependency without asking and stating why (maintenance, licence, size).

**Git**
- Trunk-based, short-lived branches, conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `ci:`, `chore:`).
- A PR merges only when CI is green.

## Never
- Broaden scope silently or rewrite unrelated files.
- Claim something works without running it.
- Produce code I could not explain line by line in an interview.

## Commands (fill in as they exist)
- API build/test: `dotnet test api`
- Web: `cd web && npm run lint && npm test && npm run build`
- Local stack: TODO (Step 4)
