namespace Bookstore.IntegrationTests;

public sealed class DbFixture : IAsyncLifetime
{
    [Fact]
    public void Container_starts() { }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}
