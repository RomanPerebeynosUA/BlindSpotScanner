namespace Bookstore.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealth(this WebApplication app) =>
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
}
