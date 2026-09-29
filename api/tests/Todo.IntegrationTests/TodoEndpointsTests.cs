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
public class TodoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;

    public TodoEndpointsTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        // Point the app's DbContext at the real, ephemeral Testcontainers Postgres
        // instead of whatever appsettings.Development.json would otherwise use.
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TodoDbContext>>();
                services.AddDbContext<TodoDbContext>(o => o.UseNpgsql(_postgres.ConnectionString));
            });
        });
    }

    [Fact]
    public async Task Create_then_list_returns_the_created_todo()
    {
        var client = await _factory.NewRegisteredClientAsync();

        var create = await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "Trim the hedges", new LocationDto("42 Example St", null, null)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await client.GetFromJsonAsync<TodoPageDto>("/todos");
        Assert.NotNull(list);
        Assert.Contains(list!.Items, t => t.Title == "Trim the hedges");
    }

    [Fact]
    public async Task List_respects_pagination_and_reports_total_count()
    {
        var client = await _factory.NewRegisteredClientAsync();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
                $"Job {i}", new LocationDto("Some address", null, null)));
        }

        var page1 = await client.GetFromJsonAsync<TodoPageDto>("/todos?page=1&pageSize=2");
        Assert.NotNull(page1);
        Assert.Equal(2, page1!.Items.Count);
        Assert.True(page1.TotalCount >= 5);
    }

    [Fact]
    public async Task Create_with_missing_title_returns_400()
    {
        var client = await _factory.NewRegisteredClientAsync();

        var response = await client.PostAsJsonAsync("/todos", new CreateTodoRequest(
            "", new LocationDto("Some address", null, null)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
