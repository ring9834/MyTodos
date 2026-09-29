# ADR-0005: Testability and maintainability — design principles, dependency injection, test doubles and designing for change

| | |
|---|---|
| **Status** | Proposed. **§3.2 (code organisation) superseded by ADR-0007**, which adopts Clean Architecture projects while keeping this ADR's rejections of MediatR, AutoMapper and repositories. **§3.3's time seam and §3.4's front-end MSW are superseded by ADR-0011** |
| **Date** | 2026-09-29 |
| **Decision area** | Quality Attributes → **Testability** and **Maintainability / modifiability** |
| **Method** | Attribute-Driven Design, using the SEI modifiability tactics (increase cohesion, reduce coupling, defer binding) and testability tactics (control and observe state, limit complexity); analysed with ATAM concepts |
| **Related** | `docs/requirements.md`: NFR-1, NFR-2, Q5, Q2, Q1, Q6, Q8, P-2, P-3; ADR-0001 (real tokens in tests); ADR-0002 (.NET, Next.js, PostgreSQL); ADR-0003 (command/query separation in code, TanStack Query); ADR-0004 (modular monolith) |

---

## 1. Context

### 1.1 Drivers

| Driver | Summary | Priority |
|---|---|---|
| **NFR-1** Testability | Automated tests cover the business rules, API behaviour and UI behaviour, and run on every change | Must |
| **Q5** Modifiability | Add a new field end to end (storage → API → UI → tests) **without AI in under 30 minutes** | M/L |
| **P-2 / P-3** | Changes will be made live without AI, and I must explain the code | Process constraint |
| **Q2** Correctness | Every legal state transition succeeds and every illegal one is rejected | H/H |
| **Q1, Q6, Q8** | Isolation, concurrency and authentication must be *proven* by tests | H/H, M/M, H/L |

### 1.2 The core tension

Maintainability and testability are often pursued by **adding abstraction**: interfaces, layers, patterns, mocks. Each abstraction has a cost: more files to change, more indirection to explain, and tests that check wiring instead of behaviour.

Q5 makes that cost concrete. **Every extra layer is another file to edit, live, without AI, against the clock.**

> **Principle:** add an abstraction only where it (1) isolates something that genuinely varies or is outside our control, or (2) is needed to test something that can't be tested for real. Otherwise, prefer the simplest direct code.

### 1.3 Measures

| Measure | Target | Source |
|---|---|---|
| Add a field end to end, by hand | < 30 min | Q5 (requirement) |
| Add a new status end to end, by hand (§3.6) | < 30 min | Derived |
| Files touched when adding a field (including migration, UI and tests) | ≤ ~12 (was ~8 before ADR-0007's layering) | Derived (change-amplification check) |
| Full test suite in CI | < 5 min | Derived (fast feedback) |
| Build warnings | 0 (warnings treated as errors) | Derived |

---

## 2. Decision summary

| Concern | Decision | Rejected |
|---|---|---|
| **Design principles** | **SOLID applied pragmatically at real seams,** together with KISS, YAGNI and high cohesion / low coupling | Dogmatic SOLID (an interface for every class) |
| **Code organisation** | ~~Vertical slices in one project~~ **Superseded by ADR-0007:** Clean Architecture projects (Api / Application / Domain / Infrastructure), with feature folders inside each layer | A repository and service layer per entity (still rejected) |
| **Data access** | **Use the ORM's context directly** (through `IAppDbContext` in Application, ADR-0007 §3.4) | A repository or unit-of-work wrapper over the ORM |
| **Mediators and mappers** | **Direct handlers and hand-written mapping** | MediatR, AutoMapper |
| **Dependency injection** | **The built-in .NET DI container**, constructor and parameter injection, with lifetimes validated at start-up | Third-party containers; service locator |
| **Test seams** | **One narrow port for testability: `IClock`** (ADR-0011 D4). `ICurrentUser` exists for security and layering; options and typed `HttpClient` are framework mechanisms | Abstractions over the ORM, the framework or pure logic |
| **Test doubles** | **Real collaborators first; hand-written fakes at seams; a mocking library only for external boundaries.** If one is needed: **NSubstitute** | Moq as the default (§3.4) |
| **Test strategy** | **Integration-heavy:** unit tests for pure rules; API integration tests with a real database as the backbone; UI component tests with network-level mocking; a small end-to-end smoke test | Mock-heavy unit tests of wiring; a coverage-percentage target |
| **API contract** | **OpenAPI as the single source of truth;** TypeScript types generated from it | Hand-maintained duplicate types |
| **State model extensibility** | **A declarative transition table in one place; the API returns each item's allowed next states** (§3.6) | The State pattern; configurable (data-driven) statuses; a state-machine library |
| **Code quality gates** | Nullable reference types; analysers; warnings as errors; formatters and linters; TypeScript `strict`; CI blocks merges on failures | — |

---

## 3. Options and trade-offs

### 3.1 SOLID: yes, where it pays

| Principle | Applied where | Not applied where |
|---|---|---|
| **S**ingle responsibility | Each feature slice does one thing; the domain rules (transition table, validators) are separate from HTTP and persistence code | Not split into many one-method classes just for the principle |
| **O**pen/closed | **At the one axis of change we can foresee:** statuses and transitions, which are data in one table (§3.6) | Not everywhere. Speculative extension points are YAGNI |
| **L**iskov substitution | Wherever an abstraction exists, its fakes must behave like the real thing, e.g. a fake current user returns a valid id | Largely moot: the design avoids inheritance hierarchies |
| **I**nterface segregation | The few interfaces are small (`ICurrentUser` has one member) | — |
| **D**ependency inversion | **Only at the seams (§3.3):** things that vary by environment or are outside our control | Not for our own concrete code. `ITodoService` with a single implementation, used only to be mocked, is rejected |

**Also applied:**
- **KISS and YAGNI:** the simplest code that meets the drivers, with no speculative generality.
- **DRY with judgement:** duplication is removed at the third occurrence (rule of three), not the second. Near-duplicates that may diverge stay separate.
- **High cohesion, low coupling:** everything for a feature lives together; modules talk through small, explicit surfaces.
- **Explicit over clever:** code that can be read top to bottom, without knowing a framework's conventions, is easier to change live (P-2).

### 3.2 Code organisation

> **Superseded by ADR-0007 §3.2.** The analysis below is kept for the record. The project now uses Clean Architecture projects; the rejections of the repository pattern, MediatR and AutoMapper in the second table still apply.

| Option | Change amplification (adding a field) | Verdict |
|---|---|---|
| **Vertical slices / feature folders** (e.g. `Todos/` holds the entity, rules, endpoints, request and response models, and mapping) | Low: one folder, plus a migration, the UI and tests | **Chosen** |
| Clean Architecture with separate projects (Domain / Application / Infrastructure / Api) | High: the entity, DTOs, commands, handlers, interfaces, repositories and mappings are spread across 4 projects | Rejected at this size. Its benefits (swappable infrastructure, strict dependency direction) don't pay off for one CRUD domain |
| Classic N-layer (controller → service → repository per entity) | Medium–high: 3–4 pass-through layers | Rejected |

**Rejected supporting patterns:**

| Pattern / library | Why rejected |
|---|---|
| **Repository / unit of work over the ORM** | The ORM's context already *is* a unit of work, and its sets already *are* repositories. A wrapper hides the features ADR-0001 and ADR-0003 rely on (global query filter, projection, change tracking control), and invites the "mock the repository" tests §3.4 warns against. The only benefit, swapping the ORM, is not a driver |
| **MediatR** (mediator / in-process messaging) | Adds indirection: navigating from an endpoint to its handler needs knowledge of the convention, which is bad for live changes (P-2). Pipeline behaviours can be done with endpoint filters. It has also moved to a commercial licence |
| **AutoMapper** | Hides the mapping, so a renamed field fails at runtime instead of at compile time. A hand-written `ToResponse()` is a few lines, is visible, and the compiler checks it. It has also moved to a commercial licence |

### 3.3 Dependency injection

| Option | Verdict | Why |
|---|---|---|
| **Built-in .NET DI container** | **Chosen** | Part of ASP.NET Core; supports everything needed (scoped, transient and singleton lifetimes; keyed services; open generics). One less dependency |
| Third-party containers (e.g. Autofac) | Rejected | Their advanced features (convention scanning, property injection, decorators) aren't needed; more concepts to explain |
| Service locator (resolving services manually inside code) | Rejected | Hides dependencies, making code harder to read and test |

**Rules:**
- Constructor injection in classes; action-level `[FromServices]` injection of use-case handlers in controllers (ADR-0007 §3.7).
- **Lifetimes:** the ORM context is *scoped* (one per request); `IClock` (`SystemClock`) is a *singleton*; the current-user service is *scoped* (it reads the request's identity).
- **Validate on start-up:** scope and build validation are enabled in development and tests, so a lifetime mistake (such as a singleton capturing a scoped context) fails immediately instead of at runtime.
- Registrations are grouped per module (`AddTodos()`, `AddAuth()`) so each module's wiring is visible in one place.

**The four seams** (the only interfaces introduced for testability):

| Seam | Abstraction | Why it varies or is outside our control | Test replacement |
|---|---|---|---|
| Time | `IClock` (one member, `UtcNow`; **superseded here by ADR-0011 D4**, was `TimeProvider`) | The real clock makes tests non-deterministic (token expiry, `createdAt`) | `FakeClock` |
| Current user | `ICurrentUser` | It comes from the request's token; ADR-0001's owner filter depends on it | The real one in integration tests (real tokens); a simple fake in unit tests |
| Configuration | Options pattern (`IOptions<T>`) | Varies by environment | Test configuration values |
| Outbound HTTP (future: geocoding) | Typed `HttpClient` | An external service we don't control | A stub `HttpMessageHandler`, or an HTTP simulator |

### 3.4 Test doubles: should we use Moq?

**Not by default.** Mocking libraries are useful for a narrow set of cases. Used broadly, they produce tests that check *how* code calls its collaborators instead of *what it does*, which makes tests brittle and refactoring expensive, and harms both drivers of this ADR.

**The order of preference for any collaborator:**

1. **The real thing**, if it's fast and deterministic: pure domain rules, validators, mapping, and the real database via Testcontainers.
2. **A hand-written fake** at a seam, e.g. `FakeClock` or a `FakeCurrentUser` class. It is simple, readable and reusable, with no library to learn.
3. **A mocking library**, only for external boundaries or for injecting failures (exceptions, timeouts) that are hard to produce for real.

**Where a mocking library cannot be used effectively:**

| Case | Why | Use instead |
|---|---|---|
| **The ORM context and its sets** (e.g. EF Core's `DbContext` / `DbSet`) | A mocked `IQueryable` runs as in-memory LINQ, so it silently skips SQL translation, global query filters (Q1!), concurrency tokens (Q6), constraints and indexes. Tests pass while production fails. The async query extensions also fail on non-async fakes | **Real PostgreSQL via Testcontainers** |
| Static members (e.g. `DateTime.UtcNow`) and extension methods | Conventional mocking libraries can't intercept them | A seam (`TimeProvider`), or test the real behaviour |
| Sealed classes and non-virtual members | Proxy-based libraries can't override them | A seam at our own boundary |
| `HttpClient` directly | Its send methods aren't virtual | Stub the `HttpMessageHandler`, or an HTTP simulator |
| The ASP.NET Core pipeline (routing, model binding, authentication, authorization, filters) | Too complex to fake faithfully | `WebApplicationFactory`, which runs the real pipeline in-process |

**Where a mocking library should not be used, even though it could be:**

| Case | Why |
|---|---|
| **Domain rules and validators** | They are pure functions, so test inputs and outputs directly |
| **Types we don't own** ("don't mock what you don't own") | A mock encodes *our assumptions* about a library's behaviour, and those assumptions are what break. Wrap it in our own adapter and test the adapter against the real thing |
| **Our own internal classes, just to isolate them** | It couples tests to the implementation; any refactor breaks tests without changing behaviour |
| **Verifying call counts and order** (`Verify(..., Times.Once)`) | Over-specified interaction tests. Assert on outcomes (response, database state) instead |
| **Authentication** | ADR-0001: integration tests use real tokens from the login endpoint, so the real security pipeline is exercised |

**Where a mocking library is appropriate:**
- Our adapter's *consumers*, when the adapter wraps an external service (e.g. "the geocoder is down, so the API returns a helpful error").
- Failure injection: exceptions, timeouts or conflicts that are impractical to produce for real.

**Which library, when one is needed:**

| Option | Notes | Verdict |
|---|---|---|
| **NSubstitute** | Concise, readable syntax; widely used | **Chosen** (used sparingly) |
| Moq | The most widely used historically. In 2023 one release bundled a dependency that collected hashed developer email addresses from git configuration during builds; it was removed after a backlash, but it's a relevant supply-chain lesson (ADR-0001 layer 8) | Acceptable if pinned to a reviewed version, but not preferred |
| FakeItEasy | Similar capability | Not needed; one library only |

**Front end:** the same philosophy applies. **Mock at the network boundary (the global `fetch`), not by mocking modules** (superseded here by ADR-0011 D5: `fetch` spies now, MSW deferred with a trigger). Components then run their real TanStack Query hooks and real fetch code against realistic HTTP responses, and tests survive refactoring.

### 3.5 Test strategy

The shape is the "testing trophy" rather than a strict pyramid: **integration tests are the backbone**, because most of this system's risk is at the boundaries (HTTP ↔ auth ↔ database).

| Level | What | Tools | Proves |
|---|---|---|---|
| **Static** | Types, nullability, analysers, linters | C# compiler (nullable, warnings as errors), analysers, TypeScript `strict`, ESLint | Whole classes of bugs never reach runtime |
| **Unit** | Pure rules: the transition table, domain guard clauses, mapping, the exception-handler table | xUnit | Q2 (all from/to pairs), BR-1 to BR-5 |
| **API integration** (the backbone) | The real HTTP pipeline + real auth + real PostgreSQL | xUnit, `WebApplicationFactory`, Testcontainers (PostgreSQL), a database reset between tests (e.g. Respawn) | Q1, Q6, Q8, Q3, NFR-2, NFR-3, the error format |
| **UI component** | Components with real hooks against a mocked network | Vitest, Testing Library (queries by role and label), `fetch` spies (ADR-0011) | FR behaviour, validation messages, optimistic rollback, NFR-9 |
| **End-to-end smoke** | One journey through the deployed system: sign in → create → schedule → complete | Playwright | The deployed wiring (Should) |

**Supporting rules:**
- **Determinism:** no sleeps; time is controlled with `FakeClock`; each test creates its own data; the database is reset between tests.
- **Test data builders** (e.g. `ATodo().Scheduled(on: date).OwnedBy(alice)`) keep tests short and make adding a field a change in one place.
- **Naming:** `Method_Scenario_ExpectedOutcome` or a readable sentence, so a failing test explains itself.
- **No coverage-percentage target.** Coverage is reviewed for *gaps in rules* (every BR and Q has a test; see the traceability matrix), not chased as a number.

### 3.6 Designing for change: "add a new status easily"

**Scenario:** a new requirement asks for another status, e.g. `in_progress` between `scheduled` and `done`, "without changing much code, or with no code change at all".

**Should this be in scope?** Yes, **but only as "cheap to change", not as "configurable".** It is the one axis of change that is easy to foresee: workflows evolve, and it is also a *likely live interview task*. Making it cheap costs almost nothing now. Making it configurable costs a great deal (see option D).

A new status is **not only data**. Someone must decide its rules: which transitions are allowed? Does it need a date (like BR-1)? Is it editable (like BR-3)? What does the gardener see? Those are **requirements decisions**. No configuration can avoid them, so "no code change" would only move the logic somewhere less visible and less tested.

| Option | Adding a status requires | Verdict |
|---|---|---|
| A. Enum + `switch`/`if` checks scattered through the code | Hunting down every check (shotgun surgery); easy to miss one | Rejected |
| **B. Enum + one declarative transition table + allowed transitions exposed by the API** | One enum value, the table rows, any rule tied to the status, a UI label and colour, and the tests (the tests force the decision). **One place in the backend; one mapping in the UI** | **Chosen** |
| C. The State pattern (a class per state) | A new class per status. Useful when states have very different *behaviour*; here they differ only in allowed transitions and one or two rules | Rejected: more ceremony for no gain |
| D. Data-driven statuses (a statuses and transitions table in the database; an admin UI) | Potentially no code change, **but**: the rules attached to statuses (BR-1, BR-2, BR-3) need a rules engine or custom code anyway; the UI must render unknown statuses; tests can't list the states; there is no admin role (A6); migrating live data between workflows is hard | Rejected (speculative generality). **Revisit** if non-developers must change workflows often, or tenants need different workflows |
| E. A state-machine library (e.g. Stateless for .NET) | Configuration through the library's API | Rejected for 3 states. Revisit if guards, entry and exit actions, or hierarchical states appear |

**How option B works:**

```csharp
// Todo.Domain/Todos/TodoTransitions.cs: the single source of truth for Q2 / BR-2
public static class TodoTransitions
{
    private static readonly Dictionary<TodoState, TodoState[]> Allowed = new()
    {
        [TodoState.Todo]      = [TodoState.Scheduled],
        [TodoState.Scheduled] = [TodoState.Todo, TodoState.Scheduled, TodoState.Done], // Scheduled→Scheduled = reschedule
        [TodoState.Done]      = [],                                                     // terminal
    };

    public static bool CanMove(TodoState from, TodoState to) => Allowed[from].Contains(to);
    public static IReadOnlyList<TodoState> AllowedFrom(TodoState from) => Allowed[from];
}
```

- **The API returns `allowedTransitions` on each item.** The UI renders the state options from that list, so **the rule lives only in the backend**. The UI never duplicates it and can't drift from it.
- **The UI has one label and colour map, with a fallback** (unknown status → its raw name, in a neutral colour). A backend-only addition therefore doesn't break the UI.
- **Guard tests make the change safe:**
  - A table-driven test lists every (from, to) pair from the enum and compares it with an explicit expected matrix. A new status fails the test until its rows are *consciously* decided.
  - A completeness test asserts that every enum value has an entry in the table.
- **Storage:** the state is stored as a string, so a new value needs no data conversion. A database check constraint, if adopted in the Data ADR, adds one migration line.

**Expected change for `in_progress`:** the enum (1 line), the table (2–3 lines), any rule (e.g. "not editable"), the expected-matrix test, and the UI label and colour, plus a migration only if there is a check constraint. That is **about 5 files, and well under 30 minutes**, which is the same budget as Q5.

### 3.7 Front-end maintainability

| Decision | Why |
|---|---|
| Feature folders (`features/todos/{components,hooks,api}`) that mirror the backend slices | A change to a feature lives in one place on each side |
| **Generated TypeScript types from the OpenAPI document** (e.g. openapi-typescript) | A backend field change shows up as a compile error in the UI instead of a runtime bug. This is a big lever for Q5 |
| Query and mutation hooks wrap TanStack Query (`useTodos`, `useChangeState`) | Components stay presentational; the data logic is testable in one place |
| One form schema per form (e.g. zod), with messages matching the API's validation | Client feedback and server validation stay aligned (NFR-2) |
| Tests query by role and label | Resilient to markup changes; also enforces accessibility (NFR-9) |

### 3.8 Code quality gates

| Gate | Where |
|---|---|
| Nullable reference types; `TreatWarningsAsErrors`; .NET analysers | Build |
| `dotnet format` / Prettier / ESLint; `.editorconfig` | Pre-commit (optional) and CI |
| TypeScript `strict` | Build |
| All tests pass; the OpenAPI-generated types are up to date | CI; merges are blocked on failure |
| Dependency updates automated (e.g. Dependabot or Renovate) | Repository |
| Architecture tests that enforce module boundaries (e.g. `Todos` doesn't reference `Auth` internals) | Should (nice to have) |

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **The transition table as the single source of truth,** also served to the UI | Modifiability, Correctness (Q2) |
| S2 | **The number of abstractions and layers** (change amplification) | Modifiability (Q5), Testability |
| S3 | **Test isolation and the database reset strategy** | Testability (reliability and speed) |
| S4 | **The OpenAPI → TypeScript generation step** | Modifiability; front-end / back-end consistency |
| S5 | **Seam placement:** too few seams make code untestable; too many make it hard to change | Testability vs Modifiability |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | Integration tests with a real database vs mock-based unit tests | Confidence that Q1, Q6 and query translation really work | Slower tests; Docker required (R1) |
| T2 | Using the ORM directly vs a repository abstraction | Fewer files; full ORM features (filters, projections) | Coupled to the ORM (swapping it is not a driver) |
| T3 | Hand-written mapping and handlers vs MediatR / AutoMapper | Explicit, compiler-checked, easy to navigate live | A little more typing |
| T4 | A declarative table vs data-driven statuses | Simplicity; tested rules; visible in code | A new status needs a (small) code change and a deployment |
| T5 | ~~Vertical slices vs Clean Architecture layers~~ | Superseded: see ADR-0007 T1 | — |
| T6 | Minimal mocking vs extensive mocking | Behaviour-focused, refactor-proof tests | Failure injection needs deliberate seams or a library |
| T7 | No coverage target vs a coverage gate | Tests aimed at rules and risks | A lower number may look worse to some reviewers (explain why) |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | **Testcontainers needs Docker.** If Docker isn't running during the interview, the integration tests can't run | Check Docker as part of the demo checklist; the unit tests run without it |
| R2 | The integration suite becomes slow as it grows | A shared container per test run; a fast database reset (not recreating it); parallelism by test collection |
| R3 | Over-engineering creeps back in ("just one more interface") | The §1.2 principle; the change-amplification measure; code review against this ADR |
| R4 | Front end and back end drift if type generation is skipped | A CI check that generated types match the current OpenAPI document |
| R5 | Coupling to the ORM makes a future data-store change expensive | Accepted: the store was chosen for its portability (ADR-0002), and the ORM supports several providers |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | Built-in DI container limitations | No feature beyond it is needed |
| N2 | Tests passing while production queries fail | The ORM is never mocked; tests use the real database engine |
| N3 | Transition rules duplicated in the UI | The UI renders `allowedTransitions` from the API |
| N4 | Unowned-type mocks breaking on library upgrades | Unowned types are never mocked |

---

## 5. Consequences

**Positive**
- Few layers and few abstractions: adding a field or a status touches a handful of files (Q5).
- Tests exercise real behaviour (HTTP, auth, SQL), so they catch the bugs that matter and survive refactoring.
- A foreseeable change (a new status) is cheap without building configurability nobody asked for.

**Negative / follow-ups**
- Docker is a hard dependency of the integration suite (R1).
- The allowed-transitions field, the generated types and the check constraint need decisions in their own areas (below).

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Component & structural** | Decided in ADR-0007: Clean Architecture projects with feature folders per layer; a pure domain core; no repository, mediator or mapper layers; controllers (`[ApiController]`) |
| **Communication & interaction** | OpenAPI is the contract; each item's response includes `allowedTransitions`; a validation error format the UI can map onto form fields |
| **Data architecture** | State stored as a string; whether to add a check constraint (integrity vs one migration line per new status); the ORM used directly |
| **Cross-cutting concerns** | The validation approach (library or built-in); `IClock` everywhere instead of the static clock (ADR-0011); options for configuration |
| **Technology & tooling** | **Finalised in ADR-0011:** xUnit v3, Shouldly, Testcontainers, Respawn, `FakeClock`, NSubstitute (sparingly), ArchUnitNET; Vitest, Testing Library, `fetch` spies (MSW deferred), Playwright; an OpenAPI → TypeScript generator; analysers and linters |
| **Deployment & operations** | CI gates: build with warnings as errors, all tests, generated-types check, lint; Docker available on the CI runners |
| **Evolution & extensibility** | A trigger for data-driven workflows (§3.6 D) and for a state-machine library (§3.6 E) |

---

## 6. Verification

| Check | How | Target |
|---|---|---|
| Add a field (Q5) | Timed manual drill, without AI | < 30 min; ≤ ~12 files (ADR-0007 §3.10) |
| Add a status (§3.6) | Timed manual drill: add `in_progress` on a branch, then discard it | < 30 min; about 5 files |
| Q2 matrix | Table-driven unit test over every (from, to) pair, plus the completeness test | Every pair covered |
| No mocked ORM | Code review; no mock of the ORM context in test projects | 0 instances |
| Suite speed | CI timing | < 5 min |
| Front-end / back-end types in sync | CI check of the generated types | Passes |

## 7. Revisit when

- Non-developers need to change workflows, or different users or tenants need different workflows. Consider data-driven statuses (§3.6 D).
- State behaviour grows (guards, entry and exit actions, notifications on transitions). Consider the State pattern or a state-machine library.
- The team grows, and module boundaries start to erode. Add architecture tests, or consider stricter layering for the affected module.
- The test suite exceeds its time budget. Revisit parallelism, and the balance between integration and unit tests.
