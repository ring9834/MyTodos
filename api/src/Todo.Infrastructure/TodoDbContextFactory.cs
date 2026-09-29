using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Todo.Infrastructure;

// Lets `dotnet ef migrations add ...` construct a DbContext at design time, without
// running the full Api's DI container / reading its runtime configuration.
// Connection string here is only used for generating migrations, never at runtime —
// the real one comes from Api's appsettings/ConfigMap/Secret.
public class TodoDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TodoDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=tododb;Username=todo;Password=localdevonly");
        return new TodoDbContext(optionsBuilder.Options);
    }
}
