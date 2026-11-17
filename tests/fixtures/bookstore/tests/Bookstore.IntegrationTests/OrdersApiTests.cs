using System.Net.Http.Json;

namespace Bookstore.IntegrationTests;

public class OrdersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Place_order_persists_lines_and_total()
    {
        var response = await _client.PostAsJsonAsync("api/orders", new { lines = new[] { new { bookId = 1, quantity = 2 } } });
        var order = await response.Content.ReadFromJsonAsync<Order>();
        Assert.Equal(2, order!.Lines.Count);
        Assert.Equal(39.98m, order.Total);
    }
}
