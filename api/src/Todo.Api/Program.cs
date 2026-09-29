using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Todo.Application.Auth;
using Todo.Application.Todos;
using Todo.Domain;
using Todo.Domain.Entities;
using Todo.Infrastructure;
using Npgsql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        npgsql.CommandTimeout(10);
    }));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<TodoService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// Liveness (no dependency check — a slow DB shouldn't cause Kubernetes to kill a healthy
// process) vs readiness (real DB check — a pod with no DB connection should stop receiving
// traffic). AddDbContextCheck is a first-party Microsoft extension, not a new dependency.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<TodoDbContext>("database", tags: ["ready"]);

// --- Auth: self-issued JWT, delivered via an
// httpOnly cookie, not the Authorization header. The JWT bearer handler's default token
// extraction only looks at the Authorization header, so OnMessageReceived is overridden to
// read it from the cookie instead — this is the one non-default piece of this setup.
const string CookieName = Program.AuthCookieName;
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured — set it via a secret, never in appsettings.json.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core silently remaps short claim names like "sub" to old,
        // verbose legacy URIs when READING an incoming token (issued as "sub", but seen by
        // application code as "http://schemas.xmlsoap.org/.../nameidentifier") — a very
        // well-known .NET gotcha. Confirmed here via every OwnerId-reading endpoint
        // throwing InternalServerError once auth itself started working correctly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,   // self-issued, single-service — no external issuer to check
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(CookieName, out var token))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<Todo.Api.Middleware.TodoExceptionHandler>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("todo-api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql()
        .AddConsoleExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddConsoleExporter());

builder.Logging.AddOpenTelemetry(o => o.AddConsoleExporter());

builder.Services.AddControllers();

var app = builder.Build();

// ADR-0004's real fix: migrations run via a Helm pre-upgrade hook Job (deploy/helm/todo-planner/
// templates/migrate-job.yaml), using this SAME image with args: ["--migrate"] instead of the
// default web-server startup. This exists because the deployed image is runtime-only (no SDK,
// no `dotnet ef` tool) — this reuses the app's own compiled EF Core dependencies directly rather
// than needing a second, SDK-based image just to run migrations. Confirmed necessary via a real
// 42P01 "relation does not exist" error — nothing had ever applied migrations against the real
// deployed database before this existed.
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await db.Database.MigrateAsync();
    return; // exit immediately — do not start the web server in this mode
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    await db.Database.MigrateAsync();
}

app.UseExceptionHandler();

// Authentication/authorization MUST be registered before any Map* calls in the minimal
// hosting model — interleaving them with endpoint mapping is a well-documented source of
// [Authorize] silently not being enforced. This ordering is unambiguous and always correct,
// regardless of the exact implicit-routing-insertion mechanics for any specific case.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();

// Exposed so Api-layer code (AuthController) and tests can issue/name the auth cookie
// consistently without a magic string duplicated in multiple places.
public partial class Program
{
    public const string AuthCookieName = "todo_session";
}
