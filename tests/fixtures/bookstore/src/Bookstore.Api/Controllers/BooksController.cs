using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Api.Controllers;

[ApiController]
[Route("api/books")]
public class BooksController(BookstoreDb db) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(db.Books);

    [HttpGet("{id}")]
    public IActionResult Get(int id) => db.Books.Find(id) is { } book ? Ok(book) : NotFound();

    [HttpPost]
    public IActionResult Create(Book book)
    {
        db.Books.Add(book);
        db.SaveChanges();
        return CreatedAtAction(nameof(Get), new { id = book.Id }, book);
    }
}
