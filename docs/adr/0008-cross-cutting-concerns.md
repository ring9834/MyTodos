# ADR-0008: Cross-cutting concerns — observability, error handling, validation, configuration and feature flags

| | |
|---|---|
| **Status** | Proposed |
| **Date** | 2026-09-29 |
| **Decision area** | **Cross-Cutting Concerns** |
| **Method** | Attribute-Driven Design; analysed with ATAM concepts |
| **Related** | `docs/requirements.md`: NFR-2, NFR-3, NFR-5, NFR-6, Q3, Q4, Q6, §10.3 (advanced monitoring deferred); ADR-0001 (log redaction, generic auth errors, secrets); ADR-0003 (per-request duration, budgets); ADR-0004 (health probes, statelessness); ADR-0005 (TimeProvider, options, test seams); ADR-0006 (retries, sensitive data, personal data); ADR-0007 (layers; error mapping in Api) |

---

## 1. Context

### 1.1 Scope

Cross-cutting concerns apply to every feature and every layer. Several were settled as tactics in earlier ADRs; this ADR chooses the **mechanisms** and where they live in the ADR-0007 layers.

| Concern | Already decided | Decided here |
|---|---|---|
| **Observability** | JSON logs to stdout; health endpoints; no secrets or personal data in logs; tracing and metrics deferred as "advanced monitoring" (requirements §10.3) | Logging library and rules; correlation; metrics and tracing approach; where telemetry goes; alerting; service-level indicators |
| **Error handling** | ProblemDetails-style errors (NFR-3); generic auth errors (ADR-0001) | The error taxonomy; the exception strategy; the mapping pipeline; the error contract the UI relies on; front-end error handling |
| **Validation** | The server is authoritative (NFR-2) | The validation mechanism and where it runs |
| **Configuration and secrets** | Options pattern (ADR-0005); secrets from the platform, never in git (ADR-0001) | Sources and precedence; validation; front-end configuration; per-environment settings |
| **Feature flags** | — | Whether to have them, and how |
| **Others** | Security (ADR-0001), caching (ADR-0003), resilience basics (ADR-0003, ADR-0006), time (ADR-0005) | Health-check mechanism; how these are wired together |

### 1.2 Drivers

| Driver | Implication |
|---|---|
| **NFR-3** Error handling | One consistent, machine-readable, standards-based error format; no internal details exposed |
| **NFR-6** Observability | Structured logs and health signals; logs queryable by field; no credentials or tokens in logs |
| **NFR-5 / ADR-0001** Security | No secrets in config files or images; no personal data in telemetry |
| **Q3 / Q7** | Latency must be measurable (per-request duration; p95) |
| **Q4** Availability | Failures must be detectable (health), and misconfiguration must never roll out |
| **Q5** Modifiability | Cross-cutting code in one place, so features don't repeat it |
| **P-5 / NFR-8** Cost | Telemetry backends and alerting must fit the trial budget |

---

## 2. Decision summary

| Concern | Decision | Rejected / deferred |
|---|---|---|
| **Logging** | **No `ILogger` in application code, except one centralised call in `TodoExceptionHandler`:** the chokepoint for exceptions that automatic instrumentation can't see. Everything else comes from OpenTelemetry instrumentation. Providers: OpenTelemetry + JSON console (§3.1) | Logging throughout the application; HTTP request logging; Serilog, NLog |
| **Correlation** | **W3C trace context, carried by OpenTelemetry** (built into ASP.NET Core): every log line, span and metric exemplar carries `TraceId`/`SpanId`. **Trace ids are not returned in error responses** (§3.2) | Custom correlation-id headers; `traceId` in ProblemDetails |
| **Metrics and tracing** | **OpenTelemetry**, automatic instrumentation (ASP.NET Core, HttpClient, Npgsql, runtime). **Export on wherever the system runs:** the standalone Aspire dashboard locally; Azure Monitor / Application Insights in the cloud, unsampled at MVP volume. Off only in automated tests | A vendor-specific SDK as the primary API; self-hosted observability stacks |
| **Log storage in the cloud** | Log records, spans and metrics exported through OpenTelemetry to Azure Monitor (short retention, daily cap); stdout JSON as the fallback (`kubectl logs`) | A self-hosted log stack (Loki, Elasticsearch) |
| **Alerting** | MVP: **a cost budget alert** (Terraform) + Kubernetes self-healing (probes, `helm --atomic`). **Alert rules defined now, enabled with the telemetry backend** (§3.4) | Paging / on-call tooling |
| **Error mechanism** | **ASP.NET Core's built-in ProblemDetails** (`AddProblemDetails()`, RFC 9457), not hand-rolled error middleware | Custom error middleware; ad-hoc error bodies |
| **Error contract** | RFC 9457 ProblemDetails + extensions: a stable machine-readable `code`, and `errors` for field validation. **No `traceId`:** the framework's default `traceId` extension is removed | Ad-hoc error bodies; `traceId` in responses |
| **Exception handling** | **`TodoExceptionHandler`** (`IExceptionHandler`, `Todo.Api.Middleware`) maps exception types to status codes in one place: `KeyNotFoundException` → 404, `InvalidTodoTransitionException` → 409, `DbUpdateConcurrencyException` → 409, `ArgumentException` → 400, anything else → 500 (§3.5). No try/catch in endpoints or handlers | Result/outcome types; per-endpoint try/catch; stack traces in responses |
| **Validation** | **Domain guard clauses** throwing `ArgumentException` (with `ParamName` = the request field) → 400 via `TodoExceptionHandler` | FluentValidation (a second place for the same rules); DataAnnotations |
| **Configuration** | **Options pattern, strongly typed, validated at start-up** (`ValidateOnStart`); precedence: `appsettings.json` → `appsettings.{Environment}.json` → environment variables (ConfigMap/Secret); **build once, deploy many** | Environment-specific builds; config values in code |
| **Secrets** | Local: user-secrets / a git-ignored `.env`. Cloud: Kubernetes Secrets populated by CI (ADR-0001) → Key Vault later | Secrets in `appsettings*.json`, images or Helm values files in git |
| **Front-end configuration** | **No environment-specific values baked into the client bundle** (the API is same-origin, so no API URL is needed); anything runtime-dependent is served by the API | `NEXT_PUBLIC_*` values that differ per environment |
| **Feature flags** | **None in the MVP.** When one is needed: `Microsoft.FeatureManagement` backed by configuration, exposed to the UI through the API; every flag has an owner and a removal date | A flag service (Azure App Configuration, LaunchDarkly, Unleash) now |
| **Health checks** | Built-in health checks: `/health/live` (process only), `/health/ready` (includes a database check through EF Core) | Liveness depending on the database (ADR-0004) |

---

## 3. Options and trade-offs

### 3.1 Logging: automatic instrumentation first, one `ILogger` call at the chokepoint

**Policy.** Application code does **not** use `ILogger`: not in Domain, Application or Infrastructure, and not in endpoints. There is exactly **one** exception, and two sources of signal replace everything else:

1. **Automatic instrumentation earns its keep at instrumented library boundaries.** The OpenTelemetry instrumentation of ASP.NET Core, HttpClient and Npgsql (§3.3) already records the key events, correlated by trace context:
   - every request: route, status and duration
   - every outbound call
   - every database command

   Writing log lines for the same events would only duplicate them.
2. **A single, centralised `ILogger` call at the one true chokepoint, `TodoExceptionHandler`,** exists specifically to cover what automatic instrumentation structurally can't reach: **plain application-level exceptions with no span around them.** The request span does exist, but once the exception handler has handled an exception, the span sees only the resulting status code. The exception's type, message and stack trace are captured only by this log record, which is correlated with the span through its trace id.

**Where each event is observed:**

| Event | Captured by | Notes |
|---|---|---|
| Request completed (route template, status, duration) | ASP.NET Core instrumentation: a span + the `http.server.request.duration` metric | Replaces HTTP request logging; gives ADR-0003's per-request duration and p95 |
| Database command | Npgsql instrumentation: a span with the parameterised statement and duration | Never parameter values |
| Outbound HTTP (future) | HttpClient instrumentation | — |
| Sign-in success or failure | The request span and metrics for `POST /api/auth/login` by status (200 / 401 / 429) | No per-user sign-in log line (see the ADR-0001 impact below) |
| Item created, updated, state changed | The request span on that route; audit timestamps in the database (ADR-0006) | No business-event log lines |
| Exception mapped to 400 / 404 / 409 | **The single `ILogger` call**, at the level in §3.5 (`Information` / `Warning`) | Exception type, mapped status, `code`, route template |
| Unexpected exception (500) | **The single `ILogger` call**, at `Error`, with the exception and stack trace | The one record of what went wrong |
| Start-up failure (e.g. options validation) | The host's own framework logging + a non-zero exit; the pod never becomes ready | Framework logging, not application code |
| Migration or seed Job failure | The runtime writes the unhandled exception to stderr; the Job fails; the Helm hook fails the release | No application logging needed |
| Runtime health (GC, thread pool) | Runtime metrics instrumentation | — |

**The call itself:**

```csharp
// TodoExceptionHandler: the only ILogger call in application code
logger.Log(level, exception, "Request failed: {Status} {Code} on {Route}", status, code, routeTemplate);
```

**Rules:**

| Rule | Why |
|---|---|
| **No `ILogger` injected anywhere else,** enforced by an architecture test (only `TodoExceptionHandler` depends on `ILogger`) | The policy holds over time, including during live changes |
| **Each exception is logged exactly once.** The framework's own diagnostics for *handled* exceptions are suppressed (`ExceptionHandlerOptions.SuppressDiagnosticsCallback`), so the handler's call is the only record | No duplicate or misleveled records |
| **Content:** exception type, status, `code`, route **template** (e.g. `/api/todos/{id}`), and the user id if authenticated. **Never** request bodies, headers, cookies, tokens, addresses or coordinates | NFR-6; ADR-0001; personal data (ADR-0006) |
| **Exception messages:** domain messages are authored and user-safe. Database errors keep Npgsql's `Include Error Detail` **off** (its default), so constraint-violation details containing data values are not included | Keeps personal data out of the one log record |
| **Structured templates,** never string interpolation | Fields are queryable (NFR-6) |
| **Framework log categories** (`Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`) at `Warning` in production; EF Core `EnableSensitiveDataLogging` **only in Development** | These are library logs, not application logging; they must stay quiet and free of data values |
| **Providers:** the OpenTelemetry logging provider (exported, trace-correlated) + the JSON console formatter (stdout, the fallback for `kubectl logs`) | The same record reaches both |

**Rejected:**

| Option | Why |
|---|---|
| Logging throughout the application (business events, handler entry and exit) | Duplicates what spans already record; more noise; more places where personal data can leak; more code to change per feature (Q5) |
| HTTP request logging middleware | Duplicates the ASP.NET Core request span and metric |
| Serilog / NLog | With one application log call, their enrichers and sinks have nothing to add |

**Consequence:** for normal traffic, spans and metrics are the *only* signal, so **telemetry export must be on wherever the system runs** (§3.3).

### 3.2 Correlation

ASP.NET Core already implements **W3C trace context**: each request gets an `Activity` with a trace id, and any incoming `traceparent` header is honoured. So:
- The JSON formatter includes scopes, so **every log line carries `TraceId` and `SpanId`.**
- **OpenTelemetry (§3.3) carries the same trace context** across logs, traces and metrics, so all telemetry for one request is already correlated on the server side.
- **Trace ids are *not* returned to clients** in error responses. `AddProblemDetails()` adds a `traceId` extension by default, so it is **removed explicitly**:

```csharp
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions.Remove("traceId"));
```

A test asserts that no error response contains `traceId` (§6), because the framework adds it by default.

| | Returning `traceId` in responses | **Not returning it (chosen)** |
|---|---|---|
| Correlation inside telemetry | Via OpenTelemetry | Via OpenTelemetry (unchanged) |
| Matching a *user's* error report to telemetry | Direct: the user quotes the id | Indirect: search telemetry by time, user id and route (R8) |
| Information exposed to clients | An internal identifier in every error | None |
| Contract surface for the UI | One more field to handle | Smaller contract |
- Custom correlation headers are **not** introduced; they would duplicate the standard.

### 3.3 Metrics and tracing

**The tension:** requirements §10.3 defers "advanced monitoring", but a production-ready system (C-8) must be *able* to see latency, errors and saturation. Instrumenting later means touching every layer; exporting later is a configuration change.

> **Decision: instrument now, and export from day one.** Application code doesn't log (§3.1), so exported telemetry is the primary signal. The requirements §10.3 deferral now covers dashboards and alert automation, not export itself.

| Aspect | Decision |
|---|---|
| **API / SDK** | **OpenTelemetry for .NET.** It is vendor-neutral: the same instrumentation can export to Azure Monitor, Prometheus/Grafana, or any OTLP backend |
| **Instrumentation** | ASP.NET Core (requests: rate, errors, duration), HttpClient (future outbound calls), Npgsql (database calls, recording the **parameterised statement, never parameter values**), .NET runtime (GC, thread pool: useful for ADR-0004 R3, the I/O-bound saturation question) |
| **Export** | **On in every running environment.** The exporter setting (`OTEL_EXPORTER_OTLP_ENDPOINT` or an Azure Monitor connection string) is **required configuration in Production**, validated at start-up (§3.8). Off only in automated tests |
| **Local** | The **standalone Aspire dashboard** container in Docker Compose receives OTLP and shows logs, traces and metrics. Free, no cloud needed, and good for the demo |
| **Cloud** | **Azure Monitor / Application Insights** through its OpenTelemetry distro. **No sampling at MVP volume,** so no error is ever sampled away; a daily ingestion cap. Sampling is added only if cost requires it, and errors are always kept |
| **Front end** | Deferred: real-user monitoring (browser errors, Web Vitals through the framework's reporting hook, sent to the same backend). The MVP relies on error boundaries and the API's server-side view |

**Rejected:**

| Option | Why |
|---|---|
| Vendor SDKs as the primary API (e.g. the classic Application Insights SDK) | Lock-in; OpenTelemetry is now the recommended path for Azure Monitor itself |
| Self-hosted Prometheus + Grafana + Tempo/Loki in the cluster | Several extra workloads on a quota-limited trial cluster (ADR-0002 R2); operational burden |
| Dashboards and APM features beyond export | Deferred per requirements §10.3; the exported data already supports them |

### 3.4 Alerting and service-level indicators

**Service-level indicators and objectives** (derived; design choices, not requirements):

| Indicator | Objective | Source |
|---|---|---|
| Availability (successful requests / all requests, excluding 4xx) | 99.5% monthly | Derived |
| List latency (p95) | < 200 ms | **Q3** |
| Other operations (p95) | < 100 ms | ADR-0003 budget |
| Error rate (5xx) | < 1% | Derived |

**Alerts:**

| Alert | Condition | MVP |
|---|---|---|
| **Cost budget** | Spending reaches 50% / 80% / 100% of the trial budget | ✅ Now (Terraform; cheap; protects the trial, NFR-8) |
| API not ready | Readiness failing for more than 5 min, or no ready replicas | Defined; enable now that export is on (Should) |
| Error rate | 5xx above 1% over 10 min | Defined; enable now that export is on (Should) |
| Latency | List p95 above 200 ms over 15 min | Defined; enable now that export is on (Should) |
| Database saturation | Database CPU > 80%, or connections > 80% of the limit (ADR-0004 S2) | Should (a managed-service metric alert; no app telemetry needed) |
| Pod restarts | Restart loop on any workload | Defined; enabled with Container Insights |
| TLS certificate expiry | Less than 14 days to expiry (ADR-0001 S5) | Defined; enabled with the edge decision |

Until those alert rules are enabled, **self-healing covers the most common failures:** probes restart unhealthy pods, readiness removes them from traffic, and `helm --atomic` rolls back failed releases.

### 3.5 Error handling: the framework's ProblemDetails + one exception handler

**Mechanism: ASP.NET Core's built-in ProblemDetails, not hand-rolled error middleware.** `AddProblemDetails()` is a first-party ASP.NET Core feature (native since .NET 8) that implements **RFC 9457**: one standard JSON shape for HTTP API errors (`type`, `title`, `status`, `detail`, `instance`), rather than each endpoint inventing its own format. Using the framework's mechanism was a deliberate early decision: no custom error middleware is written from scratch.

```csharp
// Program.cs
builder.Services.AddProblemDetails();                                            // RFC 9457 responses from the framework
builder.Services.AddExceptionHandler<Todo.Api.Middleware.TodoExceptionHandler>();  // the one place exceptions are mapped
// ...
app.UseExceptionHandler();   // invokes TodoExceptionHandler, then writes ProblemDetails
app.UseStatusCodePages();    // ProblemDetails bodies for bare error codes from the pipeline (e.g. 401, 405)
```

**Where the mapping lives: `TodoExceptionHandler`.** This is an `IExceptionHandler` implementation (in `Todo.Api/Middleware`). It catches exceptions thrown anywhere in the request pipeline and translates them into the appropriate ProblemDetails response:

| Exception | HTTP status | Why | `code` | `detail` shown to the client | Logged as |
|---|---|---|---|---|---|
| `KeyNotFoundException` | **404** | A todo doesn't exist, **or (per Q1) belongs to a different owner**. Both cases produce the identical response | `todo.not_found` | Fixed text ("The item was not found."). **Never** `ex.Message`, which can contain keys | `Information` |
| `InvalidTodoTransitionException` (Domain) | **409** | An illegal state-machine transition (e.g. `done → scheduled`, Q2); also used for editing a `done` item (BR-3) | `todo.invalid_transition` | The exception's message, which is authored in the domain and user-safe | `Information` |
| `DbUpdateConcurrencyException` (EF Core) | **409** | A stale `If-Match` / `xmin` optimistic concurrency conflict (Q6, ADR-0006) | `todo.version_conflict` | Fixed text ("This item was changed elsewhere. Reload and try again.") | `Warning` |
| `ArgumentException` (and subclasses) | **400** | Domain validation failures, e.g. a missing title, or a missing `scheduledFor` when scheduling (BR-1, BR-5); see §3.7 | `validation_failed` | Generic ("One or more fields are invalid.") + `errors` keyed by the exception's `ParamName` | `Information` |
| *Anything else (the fallback)* | **500** | Genuinely unexpected errors | `server_error` | Generic ("Something went wrong."). No exception details | `Error`, with the exception |

Errors raised by the pipeline rather than by exceptions are also ProblemDetails, through `UseStatusCodePages` and the framework:
- 401 (not authenticated; ADR-0001)
- 429 (rate limited)
- 405 / 415 (wrong method or content type)
- 428 (`precondition_required`: `If-Match` missing on an update; returned by the `[RequireIfMatch]` filter, ADR-0009 §3.5)
- 401 `auth.invalid_credentials` from a failed sign-in, returned by `AuthController` (ADR-0009 §3.3)
- 400 from `[ApiController]`'s automatic model validation, when a request can't be bound (malformed JSON, wrong types). It is given `code = validation_failed` through the ProblemDetails customisation, keeping the contract consistent

**Client cancellation** (`OperationCanceledException` when the request is aborted) is not an error: nothing is written and it's logged at `Debug`.

**Illustrative shape of the handler:**

```csharp
public sealed class TodoExceptionHandler(IProblemDetailsService problemDetails, ILogger<TodoExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, code, title, detail) = ex switch
        {
            KeyNotFoundException              => (404, "todo.not_found",          "Not found",                  "The item was not found."),
            InvalidTodoTransitionException e  => (409, "todo.invalid_transition", "This change isn't allowed.", e.Message),
            DbUpdateConcurrencyException      => (409, "todo.version_conflict",   "Conflict",                   "This item was changed elsewhere. Reload and try again."),
            ArgumentException                 => (400, "validation_failed",       "Invalid request",            "One or more fields are invalid."),
            _                                 => (500, "server_error",            "Something went wrong.",      null)
        };
        logger.Log(LevelFor(status, ex), ex, "Request failed: {Status} {Code} on {Route}", status, code, RouteOf(http)); // the only ILogger call (§3.1)
        // for ArgumentException add errors[ParamName]; then:
        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new()
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = { Status = status, Title = title, Detail = detail, Extensions = { ["code"] = code } }
        });
    }
}
```

**Exception strategy:**

| Rule | Why |
|---|---|
| **Expected rule violations are signalled by throwing specific exceptions,** and translated to HTTP in **one place** (`TodoExceptionHandler`) | Endpoints and handlers stay thin and contain only the success path; the mapping is visible in one table |
| **No try/catch in endpoints or handlers** | One place to log and format; no swallowed errors |
| **Not found:** handlers throw `KeyNotFoundException` when the owner-filtered query (ADR-0001) returns nothing | "Not yours" and "doesn't exist" are indistinguishable by construction (Q1) |
| **Concurrency:** `DbUpdateConcurrencyException` propagates from `SaveChanges` untouched | The handler maps it; no per-handler catch |
| **Domain guard clauses** use .NET's built-in guard helpers (e.g. `ArgumentException.ThrowIfNullOrWhiteSpace(title)`), with **parameter names matching request fields** | `ParamName` becomes the field key in `errors`, so the UI can highlight the right field |
| **Map from specific to general;** only the concurrency subtype of EF Core's `DbUpdateException` is mapped to 409 | Other database update failures are unexpected and fall through to 500 |
| **`detail` is never taken from `ex.Message`,** except for the domain's own exceptions | Framework and library messages can reveal internals (NFR-3) |
| **Each exception is logged once, by the handler's single `ILogger` call** (§3.1), at the level in the table. The framework's own diagnostics for handled exceptions are suppressed through `ExceptionHandlerOptions.SuppressDiagnosticsCallback`; check the default for the ASP.NET Core version in use | Error logs must mean "investigate"; no duplicate records |
| **Transient database failures** are retried by EF Core's execution strategy (ADR-0006); timeouts are bounded (ADR-0003). If they still fail, they fall through to 500 | Resilience without custom code |
| **Future outbound HTTP** (geocoding) uses the standard resilience handler (retry with jitter, timeout, circuit breaker) | Consistent resilience when external dependencies arrive |

### 3.6 The error contract (what the UI relies on)

Every non-2xx response from the API is an **RFC 9457 ProblemDetails** document produced by the framework:
- `AddProblemDetails()` provides the standard fields; its default `traceId` extension is removed (§3.2).
- `TodoExceptionHandler` adds `code`, and `errors` for 400s.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "This change isn't allowed.",
  "status": 409,
  "detail": "A completed item can't be moved to another state.",
  "code": "todo.invalid_transition"
}
```

Validation errors (400) add `errors`: a map from field name (the guard clause's `ParamName`, matching the request property, e.g. `title`, `scheduledFor`) to messages.

| Field | Rule |
|---|---|
| `code` | **Stable and machine-readable** (`todo.not_found`, `todo.invalid_transition`, `todo.version_conflict`, `validation_failed`, `auth.invalid_credentials`, `server_error`). The UI branches on `code`, never on message text, which also allows localisation later |
| `title` / `detail` | **User-safe.** Never framework or library exception messages, SQL, stack traces or internal names (NFR-3) |
| `traceId` | **Not present.** Correlation lives in telemetry (§3.2) |
| 500 responses | A generic `title` only. The details stay in the logs and traces |

**Front-end handling:**

| Situation | Handling |
|---|---|
| All API calls | **One fetch wrapper** (`web/src/lib/api`) parses ProblemDetails into a typed `ApiError { status, code, fieldErrors }` |
| Validation (400) | Field errors mapped onto form fields |
| Conflict (409) | Optimistic update rolled back (ADR-0003); for `todo.version_conflict`, a message offering to reload the latest version |
| Not found (404) | "This item no longer exists"; the list is refetched |
| Not signed in (401) | Redirect to sign-in |
| Unexpected (500, network) | A generic toast ("Something went wrong, please try again"); TanStack Query retries only idempotent GETs |
| Render errors | Next.js `error.tsx` boundaries per route segment, so one broken view doesn't blank the whole app |

### 3.7 Validation mechanism

**Decision: validation lives in the domain, as guard clauses** that throw `ArgumentException` (with `ParamName` set to the request field). These map to 400 through `TodoExceptionHandler`. No separate validation library is used.

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Domain guard clauses (`ArgumentException`)** | **One source of rules:** the entity can never be constructed or changed into an invalid state, and there's no validator to keep in sync with it; built-in .NET guard helpers; no dependency | Stops at the **first** invalid field, rather than reporting every field at once; `ArgumentException` is a broad type (R6) | **Chosen** |
| FluentValidation validators in Application, run by an endpoint filter | Reports every invalid field at once; expressive cross-field rules | A **second place** for the same rules as the entity (duplication, drift); an extra dependency; extra files per change (ADR-0007 §3.10) | Rejected (it was the earlier version of this ADR). Revisit if forms grow and all-at-once field errors matter |
| DataAnnotations / built-in minimal-API validation | Declarative | Awkward cross-field rules (BR-1, BR-2); again a second place for rules | Rejected |

**Where each check sits:**
- **Shape and invariants:** in the entity's factory and methods (`TodoItem.Create(...)`, `UpdateDetails(...)`, `ChangeState(...)`). This covers required fields, lengths, coordinate ranges and both-or-neither (BR-2), a date when scheduling (BR-1), transitions (Q2) and `done` being read-only (BR-3).
- **Existence and ownership:** in the handler (`KeyNotFoundException`).
- **Integrity backstop:** database constraints (ADR-0006).

**Front end:** each form's schema mirrors these rules and reports **every** field at once for instant feedback (ADR-0005 §3.7). This compensates for the server stopping at the first error. The server remains the authority (NFR-2).

### 3.8 Configuration

| Aspect | Decision | Why |
|---|---|---|
| **Pattern** | Strongly typed options classes (`AuthOptions`, `DatabaseOptions`, `RateLimitOptions`, `TelemetryOptions`), bound from configuration | Discoverable; testable (ADR-0005 seam) |
| **Validation** | DataAnnotations on options + custom checks (e.g. signing key ≥ 256 bits; a telemetry exporter configured in Production) with **`ValidateOnStart()`** | **Fail fast:** a misconfigured pod never becomes ready, so `helm --atomic` rolls the release back (Q4) instead of serving errors |
| **Precedence** (later wins) | `appsettings.json` (safe defaults) → `appsettings.{Environment}.json` → environment variables (ConfigMap for settings, Secret for secrets) → (later) Key Vault | Standard .NET layering; environment variables suit Kubernetes |
| **Build once, deploy many** | The same image runs in every environment; only configuration differs | What's tested is exactly what's deployed |
| **Environments** | `Development` (local), `Testing` (integration tests via `WebApplicationFactory`), `Production` (cloud) | Few environments to reason about |
| **Local secrets** | `dotnet user-secrets` for the API; a git-ignored `.env` for Docker Compose | Never in git (NFR-5) |
| **Cloud secrets** | Kubernetes Secrets populated by CI (ADR-0001 layer 7); Key Vault with the CSI driver later | — |
| **Reload** | Settings that are safe to change live (e.g. rate limits, flags) use `IOptionsMonitor`; everything else takes effect on restart | Predictable behaviour |

**Front-end configuration (Next.js):**
- `NEXT_PUBLIC_*` variables are **baked into the JavaScript bundle at build time**, which breaks "build once, deploy many".
- The API is same-origin (ADR-0001), so **the UI needs no API URL at all.**
- **Rule:** no environment-specific values in the client bundle. Anything the UI needs at runtime (feature flags, a display setting) is **served by the API.** Server-only settings (if any) are read from the environment at runtime.

### 3.9 Feature flags

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **No flags (MVP)** | Nothing to build or clean up | Every change is visible on deployment | **Chosen for now.** The MVP has no gradual rollouts or experiments |
| **Configuration-based flags (`Microsoft.FeatureManagement`)** | No extra infrastructure; flags in the ConfigMap; supports percentage and time-window filters | Changing a flag is a config change (reload or restart) | **The path when the first flag is needed** |
| A flag service (Azure App Configuration, LaunchDarkly, Unleash), possibly behind OpenFeature | Runtime changes; targeting; audit | Cost; another dependency; overkill now | Deferred. OpenFeature would keep the code vendor-neutral |

**When flags arrive, these rules apply:**
- Flags are evaluated **in the API.** The UI receives the flags it needs from the API (e.g. in `GET /auth/me`), never from build-time variables (§3.8).
- **A flag must never gate security.** Authorization is not a feature flag.
- **Every flag has an owner and a removal date,** because stale flags are technical debt.
- **Typical first uses:** dark-launching FR-11 (the map view), or hiding a new status (ADR-0005 §3.6) until the UI is ready.

### 3.10 Other cross-cutting mechanisms

| Concern | Mechanism | Layer (ADR-0007) |
|---|---|---|
| Authentication and authorization, rate limiting, security headers | ADR-0001 | Api (pipeline); Infrastructure (token issuing, hashing) |
| **Health checks** | Built-in health checks: `live` (no dependencies); `ready` (an EF Core database check). Both anonymous (ADR-0001 S7) and not exposed through the edge | Api (+ Infrastructure for the database check) |
| Time | `TimeProvider` (ADR-0005) | All except Domain, which receives times as parameters |
| Caching headers | ADR-0003 (`no-store` on API responses) | Api |
| Audit timestamps | Interceptor (ADR-0006) | Infrastructure |
| Graceful shutdown | ADR-0004 | Api host |
| Localisation | Out of scope. Stable error `code`s make it possible later | — |

**Pipeline order in `Program.cs`** (it matters, and is a sensitivity point):
exception handler → HTTPS/HSTS (when TLS terminates in the app; otherwise at the edge) → security headers → HTTP logging → rate limiting → authentication → authorization → endpoints. Health endpoints are mapped anonymously.

---

## 4. ATAM analysis

### 4.1 Sensitivity points

| # | Sensitivity point | Attributes affected |
|---|---|---|
| S1 | **Log and telemetry content discipline** (ids only; no bodies; sensitive-data logging off) | Security, Privacy |
| S2 | **The error contract** (`code`, field names); the UI depends on it | Modifiability, Usability |
| S8 | **Telemetry export being on.** With no application logging, exported spans and metrics are the only view of normal traffic | Observability, Availability (diagnosis) |
| S7 | **The exception types mapped in `TodoExceptionHandler`.** Mapping broad framework types (`ArgumentException`, `KeyNotFoundException`) means the status of *any* such exception, including one thrown by a bug, is decided by this table | Correctness, Observability |
| S3 | **Fail-fast configuration validation** | Availability (a bad config never goes live) vs deployability (a bad config blocks a release) |
| S4 | **Trace sampling rate and log retention** | Cost vs diagnostic depth |
| S5 | **Middleware pipeline order** | Security, Correctness |
| S6 | **The client-bundle configuration rule** (nothing environment-specific) | Deployability (build once, deploy many) |

### 4.2 Trade-offs

| # | Trade-off | Chosen side | Accepted cost |
|---|---|---|---|
| T1 | Built-in logging vs Serilog | Fewer dependencies; native OpenTelemetry | Fewer enrichers and sinks |
| T2 | OpenTelemetry vs a vendor SDK | Vendor neutrality; one instrumentation for any backend | Slightly more set-up |
| T3 | Instrument now, export later vs full APM now | Low cost now; observability one config change away | No historical telemetry until enabled |
| T4 | Exceptions + one central handler vs result/outcome types | Framework-native; one mapping table; thin endpoints and handlers (success path only) | Control flow through exceptions; broad types can misclassify bugs (R6); a tiny performance cost on 4xx paths (N4) |
| T5 | Domain guard clauses vs FluentValidation | One source of rules; no extra dependency; fewer files per change | The first invalid field only, not all at once (the UI schema compensates) |
| T6 | Fail-fast configuration | Misconfiguration can't reach users | A release is blocked until config is fixed (intended) |
| T7 | No feature-flag service | Nothing to run or pay for | Flag changes need a config change and reload |
| T9 | No `traceId` in responses vs returning it | Smaller contract; no internal identifiers exposed; correlation kept in OpenTelemetry | A user's error report can't be matched to telemetry directly (R8) |
| T10 | Instrumentation first + one `ILogger` call vs logging throughout the application | No duplicate signal; far less personal-data exposure; less code per feature (Q5); one enforceable rule | No business-event or per-user sign-in log lines; observability depends on export being on (R9, R10) |
| T8 | Alerts defined but deferred | Fits the trial budget | Failures beyond self-healing may go unnoticed until checked (R1) |

### 4.3 Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | **No active alerting in the MVP** (except cost): an outage self-healing can't fix may go unnoticed | Defined alert rules ready to enable; the demo checklist includes checking health and logs; the budget alert is active |
| R2 | Personal data leaks into logs or traces (EF sensitive data logging, Npgsql parameter capture or error detail, the handler's log record) | Only one application log call exists; §3.1 rules; a test that captures logs and asserts no known secret or address appears; review |
| R3 | Telemetry ingestion costs grow unexpectedly (export is always on, unsampled) | Short retention; a daily ingestion cap; the budget alert; sampling that keeps errors if needed |
| R9 | **Export disabled or misconfigured leaves the system nearly blind:** only framework warnings and the handler's exception records reach stdout | The exporter is required configuration in Production, validated at start-up (fail fast); health endpoints still work |
| R10 | **Weaker security detection:** no per-user sign-in log (ADR-0001 detection layer) | Watch 401 and 429 rates on the sign-in route (request metrics) and alert on spikes; add an authentication audit log if real users are onboarded |
| R4 | Exception messages leak into responses | One global handler with generic 500 bodies; a test asserting no stack trace or exception text |
| R5 | Environment values are baked into the Next.js bundle by mistake | The §3.8 rule; review for `NEXT_PUBLIC_*` usage |
| R6 | **Broad exception types misclassify bugs.** A `KeyNotFoundException` from a dictionary lookup bug (e.g. a status missing from `TodoTransitions`) becomes a 404; an `ArgumentException` thrown by library code becomes a 400. Both are logged at `Information` instead of `Error` | The transition-table completeness test (ADR-0005 §3.6) covers the most likely case; mapped exceptions are logged with their type and stack at `Information`. **Optional narrowing that keeps this table:** throw domain subclasses (e.g. `TodoNotFoundException : KeyNotFoundException`, `TodoValidationException : ArgumentException`) and map only those, so framework throws fall through to 500 |
| R8 | **A user's error report can't be matched directly to its trace,** because no id is shown to the user | Search telemetry by time, user id and route (all present in logs and spans). Revisit if support needs a direct link: re-enable `traceId`, or return a trace response header |
| R7 | Handled 4xx exceptions are also logged as errors by the framework's diagnostics, creating noise | Configure `SuppressDiagnosticsCallback` (§3.5); check in the log-capture test |

### 4.4 Non-risks

| # | Non-risk | Because |
|---|---|---|
| N1 | Missing correlation between log lines of one request | Trace context is built in and included in every line |
| N2 | Log volume at this scale | Few users; `Information` by default; no body logging |
| N3 | Inconsistent error formats across endpoints | The framework's ProblemDetails, one exception handler, and status-code pages for pipeline errors |
| N4 | The performance cost of exceptions on 4xx paths | Microseconds per request; irrelevant to the ADR-0003 budgets at this scale |

---

## 5. Consequences

**Positive**
- One place each for logging configuration and error mapping (`TodoExceptionHandler` on the framework's ProblemDetails), so features don't repeat cross-cutting code (Q5).
- One source of validation rules (the domain), with no validator to keep in sync.
- A stable, minimal error contract the UI can depend on; request correlation handled by OpenTelemetry.
- Observability is vendor-neutral and one configuration change away from full metrics and traces.
- Misconfiguration never reaches users.

**Negative / follow-ups**
- Alerting beyond cost is deferred (R1).
- A log-capture test and a no-stack-trace test are needed to protect S1 and NFR-3.
- The broad exception types need the R6 safeguards.

### Impacts on other decision areas

| Decision area | Constraint or input from this decision |
|---|---|
| **Communication & interaction** | The status codes in §3.5 (404 / 409 / 400 / 500, plus 401 / 429 from the pipeline); the ProblemDetails contract (`code`, `errors`; no `traceId`); field names matching request properties; the version travels in `If-Match` (resolved in ADR-0009 §3.5) |
| **Deployment & operations** | ConfigMap and Secret layout; OpenTelemetry exporter settings (**required in Production**; Aspire dashboard endpoint locally); Aspire dashboard in Docker Compose; the budget alert in Terraform; Container Insights / Log Analytics (Should) with retention and a daily cap; alert rules ready to enable |
| **Component & structural** | `Todo.Api/Middleware/TodoExceptionHandler.cs`; `InvalidTodoTransitionException` in `Todo.Domain`; guard clauses in entities; no validators or result types; options classes next to their consumers; telemetry set-up in Api; an architecture test allowing `ILogger` only in `TodoExceptionHandler` |
| **Technology & tooling** | The logging abstractions only in `TodoExceptionHandler`; OpenTelemetry logging provider + JSON console; OpenTelemetry .NET (+ ASP.NET Core, HttpClient, Npgsql, runtime instrumentation; OTLP and Azure Monitor exporters); ASP.NET Core ProblemDetails + `IExceptionHandler`; `Microsoft.FeatureManagement` (when needed); the Aspire dashboard container |
| **Security (ADR-0001)** | The detection layer changes from per-user sign-in logs to sign-in-route request metrics (401 / 429 rates); log content rules implement NFR-6; generic error bodies; flags never gate security |
| **Evolution & extensibility** | Dashboards and alert automation on the exported data; front-end real-user monitoring; a flag service behind OpenFeature; Key Vault for configuration secrets |

---

## 6. Verification

| Check | How |
|---|---|
| Each row of the §3.5 table produces its status and `code`: not found / not yours → 404; illegal transition → 409; stale version → 409; missing title or scheduled without a date → 400 with `errors[field]` | Integration tests (Q1, Q2, Q6, BR-1, BR-5) |
| Unexpected exception → generic 500 ProblemDetails, no stack trace or exception text | Integration test (an endpoint that throws, in the test host only) |
| `TodoExceptionHandler` mapping | Unit test over the exception → (status, code) table |
| 401 / 429 responses are ProblemDetails too | Integration tests |
| No secrets or personal data in logs | Integration test with a log-capture provider: sign in, create an item with a known address, then assert the password, token, cookie and address never appear in captured logs |
| Sensitive-data logging disabled outside Development | Configuration test |
| Missing or invalid configuration → the app fails to start | Test: start the host without the signing key and assert a start-up failure |
| Every log line carries `TraceId` | Inspect the JSON output locally |
| **`ILogger` is used only in `TodoExceptionHandler`** | Architecture test |
| **One exception → exactly one log record,** correlated with the request's span (a 500 at `Error` with the stack; a 404 at `Information`); no duplicate framework record | Integration test with a log-capture provider |
| Requests and database calls appear as spans without any application logging | Locally, in the Aspire dashboard |
| Missing exporter configuration in Production → the app fails to start | Configuration test |
| **No error response contains `traceId`** (the framework default is removed) | Integration test across 400 / 401 / 404 / 409 / 500 |
| Telemetry works end to end | Locally: requests, database spans and metrics visible in the Aspire dashboard |
| Budget alert exists | `terraform plan` shows the budget and its thresholds |

## 7. Revisit when

- The system goes beyond the demo. Enable the telemetry export, Container Insights and the defined alert rules; add front-end real-user monitoring.
- The first gradual rollout or experiment is needed. Introduce `Microsoft.FeatureManagement`; move to a flag service (via OpenFeature) when non-developers need to change flags.
- Outbound dependencies appear (geocoding). Add the standard HTTP resilience handler and outbound-call telemetry.
- Localisation is needed. Map error `code`s to translated messages in the UI.

---

## Change log

| Date | Change |
|---|---|
| 2026-09-29 | First version: business rule violations as outcome types; FluentValidation via an endpoint filter |
| 2026-09-29 | **Revised at the author's decision:** ASP.NET Core's built-in ProblemDetails (`AddProblemDetails()`, RFC 9457) with one `IExceptionHandler` (`TodoExceptionHandler`) mapping exception types to status codes; validation by domain guard clauses (`ArgumentException` → 400). Consequential changes in ADR-0007 |
| 2026-09-29 | **Revised at the author's decision:** `traceId` removed from error responses (the framework default is stripped in `CustomizeProblemDetails`); correlation is handled by OpenTelemetry. Added T9, R8 and a test |
| 2026-09-29 | **Revised at the author's decision:** no `ILogger` in application code except one centralised call in `TodoExceptionHandler` (covering exceptions that automatic instrumentation can't reach); everything else from OpenTelemetry instrumentation. Consequently: export on wherever the system runs (required in Production, unsampled at MVP volume); HTTP request logging removed; ADR-0001's detection layer moves to sign-in-route metrics. Added S8, T10, R9, R10 and tests |
