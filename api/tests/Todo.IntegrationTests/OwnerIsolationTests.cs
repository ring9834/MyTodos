using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Todo.Api.Controllers;
using Todo.Application.Todos.Dtos;
using Todo.Infrastructure;
using Xunit;

namespace Todo.IntegrationTests;

// Two genuinely different, independently authenticated users, one
// trying to reach the other's data. This is the test that actually matters in this slice —
// everything else in the project could be correct and this could still fail if ownership
// filtering has any gap.
[Collection("Postgres")]
public class OwnerIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OwnerIsolationTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TodoDbContext>>();
                services.AddDbContext<TodoDbContext>(o => o.UseNpgsql(postgres.ConnectionString));
            });
        });
    }

    // Each client below is a genuinely separate HttpClient with its own cookie jar
    // (CreateClient's default HttpClientHandler tracks cookies per-client), so this
    // faithfully represents two different browsers/users, not one client reused.
    private async Task<HttpClient> NewRegisteredClientAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TodoDbContext>().Database.MigrateAsync();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await client.PostAsJsonAsync("/auth/register", new AuthRequest(username, "correct horse battery staple"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task User_A_cannot_read_user_Bs_todo()
    {
        var clientA = await NewRegisteredClientAsync($"alice-{Guid.NewGuid():N}");
        var clientB = await NewRegisteredClientAsync($"bob-{Guid.NewGuid():N}");

        var bobsTodo = await (await clientB.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Bob's private job", new LocationDto("Bob's address", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        // The core assertion: 404, never 403 — per Q1, don't even confirm the resource exists.
        var response = await clientA.GetAsync($"/todos/{bobsTodo!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task User_A_cannot_update_user_Bs_todo()
    {
        var clientA = await NewRegisteredClientAsync($"alice-{Guid.NewGuid():N}");
        var clientB = await NewRegisteredClientAsync($"bob-{Guid.NewGuid():N}");

        var bobsTodo = await (await clientB.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Bob's private job", new LocationDto("Bob's address", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/todos/{bobsTodo!.Id}")
        {
            Content = JsonContent.Create(new UpdateTodoRequest("Hijacked", new LocationDto("x", null, null)))
        };
        request.Headers.Add("If-Match", $"\"{bobsTodo.ETag}\"");

        var response = await clientA.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task User_A_cannot_delete_user_Bs_todo()
    {
        var clientA = await NewRegisteredClientAsync($"alice-{Guid.NewGuid():N}");
        var clientB = await NewRegisteredClientAsync($"bob-{Guid.NewGuid():N}");

        var bobsTodo = await (await clientB.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Bob's private job", new LocationDto("Bob's address", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        var response = await clientA.DeleteAsync($"/todos/{bobsTodo!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // And confirm Bob's todo is genuinely untouched, not just that A got a 404.
        var stillThere = await clientB.GetAsync($"/todos/{bobsTodo.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task User_As_own_list_never_contains_user_Bs_todos()
    {
        var clientA = await NewRegisteredClientAsync($"alice-{Guid.NewGuid():N}");
        var clientB = await NewRegisteredClientAsync($"bob-{Guid.NewGuid():N}");

        await clientB.PostAsJsonAsync("/todos", new CreateTodoRequest("Bob's job", new LocationDto("x", null, null)));
        await clientA.PostAsJsonAsync("/todos", new CreateTodoRequest("Alice's job", new LocationDto("y", null, null)));

        var alicesList = await clientA.GetFromJsonAsync<TodoPageDto>("/todos");

        Assert.All(alicesList!.Items, t => Assert.Equal("Alice's job", t.Title));
    }

    [Fact]
    public async Task Requests_with_no_session_are_rejected()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/todos");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
