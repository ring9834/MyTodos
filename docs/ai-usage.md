# AI-Enabled Engineering Log

This log records how AI tools were used to build this project, what was accepted, changed or rejected, and how the output was verified. The aim is to show that AI accelerated the work while design decisions, correctness and understanding stayed with me.

> **Honesty rule for this log:** it records what actually happened, including AI mistakes and my own. Placeholders marked _[fill in]_ need completing in my own words; nothing here should claim work or checks I didn't do.

---

## 1. Working principles

1. **I own the decisions.** AI proposes options and I choose. Business assumptions live in `requirements.md`, and technical decisions in ADRs, both in my own reasoning.
2. **Nothing is committed unread.** I read and understand every generated line before committing it. If I can't explain it, it doesn't go in.
3. **Verification over trust.** The gate for AI output is evidence: the source documents, tests, compiler and linters, `terraform plan`, `helm lint`, and running the system. The AI's confidence is not evidence.
4. **Challenge the output.** I question structure and reasoning, not just syntax. For example, is this requirement actually a design decision?
5. **Small, reviewable steps.** One change per prompt, with small commits and clear messages.
6. **No secrets or private data in prompts.** No connection strings, keys, passwords or tokens are pasted into AI tools.
7. **Keep hands-on capability.** I rehearse likely changes by hand, because the interview requires unassisted changes (Q5).

## 2. Workflow

```
Define the task myself  →  Prompt for one small piece  →  Read and question the output
        ↑                                                          ↓
  Log it here  ←  Commit  ←  Verify against evidence (source docs / tests / plan / run)
```

## 3. Tools

| Tool | Used for | Not used for |
|---|---|---|
| Claude (claude.ai chat) | Requirements analysis, document drafting, design discussion, reviewing my own drafts | Final decisions; anything involving secrets |
| _[fill in: IDE assistant, e.g. Copilot / Cursor / Claude Code]_ | _[fill in: e.g. code completion, boilerplate, test scaffolding]_ | _[fill in]_ |

---

## 4. Log

### Phase 0: Planning and requirements analysis (2026-09-29)

**4.1 End-to-end plan**
- **Asked AI to:** produce an end-to-end plan from requirements through to deployment, based on the two brief documents.
- **Accepted:** the phase structure (requirements → architecture → backend → frontend → containers → Terraform → Helm → CI/CD → readiness → rehearsal) and the prioritisation principle of a thin vertical slice through every layer.
- **Changed / rejected:** the AI assumed a stack (React + Vite, single-user, fully permissive state transitions) without asking. I replaced these with my own decisions (see 4.4).
- **Verified by:** checking that every mandated technology in the brief (Terraform, Kubernetes, Helm, CRUD API, tests) appears in the plan.

**4.2 Which documents to prepare**
- **Asked AI to:** recommend which documents to show at the interview.
- **Accepted:** a small set (README, requirements, architecture, ADRs, this log, production-readiness notes), with each document kept to about one page.
- **Changed:** _[fill in, if anything]_

**4.3 Requirements vs design boundary**
- **Asked AI to:** draft `requirements.md` and ADR-0001 (technology stack).
- **Problem I spotted:** the first draft mixed requirements with design. It included the API contract (endpoints, status codes, JSON), the physical data model (UUIDs, index, concurrency token), technology choices in the constraints table, and technology-specific NFRs (e.g. RFC 7807, `helm --atomic`).
- **How I challenged it:** I asked whether the API contract, state model and data model were requirements or design. The resulting test was *"Would the business care if this changed?"* If not, it's design.
- **Outcome:**
  - Requirements now state *what* and *why* only.
  - The state model and conceptual data model stayed, because they are business rules.
  - The API contract and physical data model moved to Phase 1 design.
  - ADR-0001 moved to Phase 1, because an ADR records a design decision.
- **Verified by:** re-reading each section of `requirements.md` against the test above.

**4.4 My assumptions and quality attribute scenarios**
- **Input:** I supplied my own assumptions (A1–A7) and quality attribute scenarios (Q1–Q7). _[fill in: written by me / drafted with another tool and edited by me]_
- **Where my decisions overrode the AI's earlier draft:**

  | Topic | AI's draft | My decision | Why |
  |---|---|---|---|
  | Users | Single user, auth deferred | Multi-user from day one; every item has an owner | Cheap now, expensive to retrofit; gives a real security scenario |
  | State transitions | All transitions allowed | `todo → scheduled → done`, plus unschedule; nothing leaves `done` | Simplest rule that supports the domain; clear and testable |
  | Frontend | React + Vite | Next.js (React-based, satisfies C-1) | _[fill in your reason]_ |
  | NFR style | Flat NFR list | Measurable quality attribute scenarios with priorities | Testable, and ranked by importance and risk |

- **Asked AI to:** rethink and regenerate `requirements.md` around my assumptions and scenarios.
- **Accepted from the AI's review:**
  - It found that the brief allows **creation in any state, including `done`**. So creation is not a transition, and the state diagram now has three entry points.
  - It filled in my incomplete **Q7 (scalability)**.
  - It added **Q8 (unauthenticated access)**, a gap in my scenarios once multi-user was in scope.
  - It kept design details out of my scenarios (`xmin`, index, status codes, `helm --atomic`) while preserving the intent, e.g. "indistinguishable from not found".
  - It added a risk for credentials sent over plain HTTP while HTTPS is deferred.
- **Verified by:**
  - _[✓ when done]_ I checked the "creation in any state" claim against the source text: the brief says to allow creation *"with … a state of 'todo', 'scheduled' and 'done'"*.
  - _[✓ when done]_ I re-read both brief documents line by line against constraints C-1 to C-8 and P-1 to P-7, to confirm nothing was missed or invented.
  - _[✓ when done]_ I checked that every Q and FR appears in the traceability matrix.
- **Open decisions (mine to make, not the AI's):**
  - _[ ]_ Can `todo` go straight to `done`? (currently: no)
  - _[ ]_ Can a `done` item be edited or deleted? (currently A9: delete only)
  - _[ ]_ Is scheduling by date, or by date and time? (currently: date)

### Phase 1: Architecture and design

- **Asked AI to:**
- **Accepted:**
- **Changed / rejected:**
- **Verified by:**

<!-- Copy this block for each later phase: backend, tests, frontend, containers, Terraform, Helm, CI/CD, readiness -->

---

## 5. Summary of corrections and rejections

This is the quick reference for the interview: where I didn't simply accept the AI's output.

| # | Phase | What the AI produced | What I did | Evidence |
|---|---|---|---|---|
| 1 | 0 | Design details mixed into requirements | Challenged the boundary; design moved to Phase 1 | Log 4.3; `requirements.md` header |
| 2 | 0 | ADR drafted during requirements | Deferred to Phase 1 (an ADR records a design decision) | Log 4.3 |
| 3 | 0 | Single-user, permissive state transitions, React + Vite | Replaced with my own assumptions A3, A4, A5 | Log 4.4 |
| 4 | _[n]_ | _[fill in as the build progresses]_ | | |

## 6. Where AI was deliberately not relied on

- Business assumptions and their final confirmation (`requirements.md` section 4).
- Scope and prioritisation decisions (`requirements.md` section 10).
- Business rules BR-1 to BR-6 and the tests that prove them. _[fill in: e.g. I wrote the state-transition tests by hand]_
- Reviewing every `terraform plan` before applying it.
- Anything involving credentials or secrets.
