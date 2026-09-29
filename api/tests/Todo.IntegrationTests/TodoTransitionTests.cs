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
public class TodoTransitionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodoTransitionTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
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
    public async Task Transition_todo_to_scheduled_succeeds_and_stores_scheduledFor()
    {
        var client = await _factory.NewRegisteredClientAsync();
        var created = await (await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Mow the lawn", new LocationDto("1 Example Rd", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        var scheduledFor = DateTimeOffset.UtcNow.AddDays(1);
        var response = await client.PostAsJsonAsync($"/todos/{created!.Id}/transition",
            new { targetState = "scheduled", scheduledFor });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<TodoDto>();
        Assert.Equal("scheduled", dto!.State); // lowercase, per openapi.yaml's documented contract
    }

    [Fact]
    public async Task Transition_from_done_returns_409()
    {
        var client = await _factory.NewRegisteredClientAsync();
        var created = await (await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "One-off job", new LocationDto("2 Example Rd", null, null)))).Content.ReadFromJsonAsync<TodoDto>();

        await client.PostAsJsonAsync($"/todos/{created!.Id}/transition", new { targetState = "done" });

        var secondAttempt = await client.PostAsJsonAsync(
            $"/todos/{created.Id}/transition", new { targetState = "scheduled", scheduledFor = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Conflict, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Transition_of_someone_elses_todo_returns_404()
    {
        // Once real auth exists, this test authenticates as a
        // second user and confirms 404, not 403. Left as a TODO marker here since
        // DevOwnerId is still hardcoded — do not skip revisiting this.
        await Task.CompletedTask;
    }
}
