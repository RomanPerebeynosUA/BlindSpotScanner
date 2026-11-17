using System.Net;
using System.Net.Http.Json;

namespace Bookstore.IntegrationTests;

public class BooksApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task List_returns_seeded_books()
    {
        var books = await _client.GetFromJsonAsync<Book[]>("api/books");
        Assert.Equal(3, books!.Length);
    }

    [Fact]
    public async Task Get_by_id_returns_ok()
    {
        var response = await _client.GetAsync("api/books/1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_persists_book()
    {
        var response = await _client.PostAsJsonAsync("api/books", new { title = "Dune" });
        var created = await response.Content.ReadFromJsonAsync<Book>();
        Assert.Equal("Dune", (await _client.GetFromJsonAsync<Book>($"api/books/{created!.Id}"))!.Title);
    }

    [Fact]
    public async Task Create_rejects_missing_title()
    {
        var response = await _client.PostAsJsonAsync("api/books", new { title = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
