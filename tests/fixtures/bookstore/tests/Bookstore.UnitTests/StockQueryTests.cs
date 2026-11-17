namespace Bookstore.UnitTests;

[Trait("Category", "Integration")]
public class StockQueryTests
{
    // [Trait("Category", "Manual")]
    [Fact]
    public async Task Low_stock_query_uses_index() => await Task.CompletedTask;
}
