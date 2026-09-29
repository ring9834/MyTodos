using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Todo.Api.Controllers;
using Todo.Infrastructure;

namespace Todo.IntegrationTests;

// Shared by every test class that needs an authenticated client — Slices tests
// predate auth entirely and all needed this once [Authorize] started genuinely
// protecting /todos. Centralized here instead of duplicated per file.
public static class AuthTestHelpers
{
    public static async Task<HttpClient> NewRegisteredClientAsync(
        this WebApplicationFactory<Program> factory, string usernamePrefix = "user")
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.MigrateAsync();

        // Each HttpClient gets its own cookie jar (HandleCookies), so this genuinely
        // represents one authenticated session, not a client reused across identities.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await client.PostAsJsonAsync(
            "/auth/register", new AuthRequest($"{usernamePrefix}-{Guid.NewGuid():N}", "correct horse battery staple"));
        response.EnsureSuccessStatusCode();
        return client;
    }
}
