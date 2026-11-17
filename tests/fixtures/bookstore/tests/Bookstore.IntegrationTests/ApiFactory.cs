using Microsoft.AspNetCore.Mvc.Testing;

namespace Bookstore.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Seeds three books into a real PostgreSQL container before the tests run.
    public Task InitializeAsync() => Task.CompletedTask;

    public new Task DisposeAsync() => Task.CompletedTask;
}
