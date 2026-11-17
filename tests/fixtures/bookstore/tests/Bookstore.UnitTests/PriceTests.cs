namespace Bookstore.UnitTests;

public class PriceTests
{
    [Fact]
    public void Total_sums_lines() => Assert.Equal(30m, Price.Total([10m, 20m]));
}
