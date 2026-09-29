using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Todo.Application.Todos.Dtos;
using Todo.Infrastructure;
using Xunit;

namespace Todo.IntegrationTests;

[Collection("Postgres")]
public class TodoUpdateDeleteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodoUpdateDeleteTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
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

    

    [Fact]
    public async Task Update_with_correct_ETag_succeeds()
    {
        var client = await _factory.NewRegisteredClientAsync();
        var created = await (await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Weed the beds", new LocationDto("1 Example Rd", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/todos/{created!.Id}")
        {
            Content = JsonContent.Create(new UpdateTodoRequest("Weed the front beds", new LocationDto("1 Example Rd", null, null)))
        };
        request.Headers.Add("If-Match", $"\"{created.ETag}\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_with_stale_ETag_returns_409()
    {
        // Two clients read the same todo, one updates first, the second's
        // update (using the now-stale ETag it originally read) must be rejected — this is
        // what actually proves the xmin-based optimistic concurrency check works end to end.
        var client = await _factory.NewRegisteredClientAsync();
        var created = await (await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Rake leaves", new LocationDto("2 Example Rd", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        // First update succeeds, changing the row's real xmin.
        var firstUpdate = new HttpRequestMessage(HttpMethod.Put, $"/todos/{created!.Id}")
        {
            Content = JsonContent.Create(new UpdateTodoRequest("Rake all leaves", new LocationDto("2 Example Rd", null, null)))
        };
        firstUpdate.Headers.Add("If-Match", $"\"{created.ETag}\"");
        await client.SendAsync(firstUpdate);

        // Second update still uses the ORIGINAL (now stale) ETag — must be rejected.
        var secondUpdate = new HttpRequestMessage(HttpMethod.Put, $"/todos/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateTodoRequest("A different edit", new LocationDto("2 Example Rd", null, null)))
        };
        secondUpdate.Headers.Add("If-Match", $"\"{created.ETag}\""); // stale on purpose

        var response = await client.SendAsync(secondUpdate);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_without_If_Match_header_returns_400()
    {
        var client = await _factory.NewRegisteredClientAsync();
        var created = await (await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Prune roses", new LocationDto("3 Example Rd", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        var response = await client.PutAsJsonAsync($"/todos/{created!.Id}",
            new UpdateTodoRequest("Prune all roses", new LocationDto("3 Example Rd", null, null)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_then_delete_again_is_idempotent_second_call_returns_404()
    {
        var client = await _factory.NewRegisteredClientAsync();
        var created = await (await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "One-off job", new LocationDto("4 Example Rd", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        var first = await client.DeleteAsync($"/todos/{created!.Id}");
        var second = await client.DeleteAsync($"/todos/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }
}
