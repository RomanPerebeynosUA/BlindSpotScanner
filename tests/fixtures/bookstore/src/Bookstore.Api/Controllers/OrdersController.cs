using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService orders) : ControllerBase
{
    [HttpPost]
    public IActionResult Place(PlaceOrder request) => Ok(orders.Place(request));

    [HttpPost("{id}/cancel")]
    public IActionResult Cancel(int id) => orders.Cancel(id) ? NoContent() : NotFound();
}
