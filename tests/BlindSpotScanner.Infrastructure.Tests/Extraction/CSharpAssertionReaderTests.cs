using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

public class CSharpAssertionReaderTests
{
    private static readonly CSharpAssertionReader Reader = new();

    /// <summary>One test method named T.Test with the given body, plus optional extra members of class T.</summary>
    private static TestAssertionFacts Single(string body, string members = "") =>
        Reader.Read($$"""
            public class T
            {
                [Fact]
                public async Task Test()
                {
                    {{body}}
                }

                {{members}}
            }
            """).Single();

    private static string Bookstore(string path) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "bookstore", path));

    [Fact]
    public void Read_EmptyTest_IsL0()
    {
        // Arrange
        // Act
        var facts = Single("");

        // Assert
        facts.Level.Should().Be(0);
    }

    [Fact]
    public void Read_StatusCodeOnly_IsL1()
    {
        // Arrange
        const string body = """
            var response = await _client.GetAsync("api/books/1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(1);
    }

    [Fact]
    public void Read_EnsureSuccessStatusCode_IsL1()
    {
        // Arrange
        const string body = """
            var response = await _client.GetAsync("api/books/1");
            response.EnsureSuccessStatusCode();
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(1);
    }

    [Fact]
    public void Read_FluentBeSuccessfulOnResponse_IsL1()
    {
        // Arrange
        const string body = """
            var response = await _client.GetAsync("api/books/1");
            response.Should().BeSuccessful();
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(1);
    }

    [Fact]
    public void Read_LengthOnly_IsL2()
    {
        // Arrange
        const string body = """
            var books = await _client.GetFromJsonAsync<Book[]>("api/books");
            Assert.Equal(3, books!.Length);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(2);
    }

    [Fact]
    public void Read_DbCountAsync_IsL2()
    {
        // Arrange
        const string body = """
            var resp = await client.PostAsync("/orders", payload);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await db.Orders.CountAsync()).Should().Be(1);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(2);
    }

    [Fact]
    public void Read_ResponseValue_IsL3()
    {
        // Arrange
        const string body = """
            var response = await _client.PostAsJsonAsync("api/orders", new { quantity = 2 });
            var order = await response.Content.ReadFromJsonAsync<Order>();
            Assert.Equal(39.98m, order!.Total);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(3);
    }

    [Fact]
    public void Read_GetAfterPost_IsL4ViaApi()
    {
        // Arrange
        const string body = """
            var response = await _client.PostAsJsonAsync("api/books", new { title = "Dune" });
            var created = await response.Content.ReadFromJsonAsync<Book>();
            var read = await _client.GetFromJsonAsync<Book>($"api/books/{created!.Id}");
            Assert.Equal("Dune", read!.Title);
            """;

        // Act
        var facts = Single(body);

        // Assert
        new { facts.Level, facts.ReadBack }.Should().BeEquivalentTo(new { Level = (int?)4, ReadBack = (ReadBack?)ReadBack.Api });
    }

    [Fact]
    public void Read_GetWithoutWrite_IsL3()
    {
        // Arrange
        const string body = """
            var book = await _client.GetFromJsonAsync<Book>("api/books/1");
            Assert.Equal("Dune", book!.Title);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(3);
    }

    [Fact]
    public void Read_DbReadValue_IsL4ViaDb()
    {
        // Arrange
        const string body = """
            await _client.PostAsJsonAsync("api/books", new { title = "Dune" });
            var saved = await db.Books.SingleAsync();
            saved.Title.Should().Be("Dune");
            """;

        // Act
        var facts = Single(body);

        // Assert
        new { facts.Level, facts.ReadBack }.Should().BeEquivalentTo(new { Level = (int?)4, ReadBack = (ReadBack?)ReadBack.Db });
    }

    [Fact]
    public void Read_WireMockLogEntries_IsL5()
    {
        // Arrange
        const string body = """
            await _client.PostAsJsonAsync("api/orders", new { quantity = 2 });
            _payments.LogEntries.Should().ContainSingle();
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(5);
    }

    [Fact]
    public void Read_ShouldlyShouldBe_IsL3()
    {
        // Arrange
        const string body = """
            var book = await _client.GetFromJsonAsync<Book>("api/books/1");
            book!.Title.ShouldBe("Dune");
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(3);
    }

    [Fact]
    public void Read_NUnitThatIsNotNull_IsL2()
    {
        // Arrange
        const string body = """
            var book = await _client.GetFromJsonAsync<Book>("api/books/1");
            Assert.That(book, Is.Not.Null);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().Be(2);
    }

    [Fact]
    public void Read_SameFileHelperAssertsValue_IsL3()
    {
        // Arrange
        const string body = """
            var book = await _client.GetFromJsonAsync<Book>("api/books/1");
            AssertBook(book!);
            """;
        const string helper = """
            private static void AssertBook(Book b) => Assert.Equal("Dune", b.Title);
            """;

        // Act
        var facts = Single(body, helper);

        // Assert
        facts.Level.Should().Be(3);
    }

    [Fact]
    public void Read_UnresolvedAssertionHelper_IsUnknown()
    {
        // Arrange
        const string body = """
            var order = await _client.GetFromJsonAsync<Order>("api/orders/1");
            Assert.NotNull(order);
            AssertOrderMatches(order!);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.Level.Should().BeNull();
    }

    [Fact]
    public void Read_BadRequestStatus_IsErrorPath()
    {
        // Arrange
        const string body = """
            var response = await _client.PostAsJsonAsync("api/books", new { title = "" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.ErrorPath.Should().BeTrue();
    }

    [Fact]
    public void Read_OkStatus_IsNotErrorPath()
    {
        // Arrange
        const string body = """
            var response = await _client.GetAsync("api/books/1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.ErrorPath.Should().BeFalse();
    }

    [Fact]
    public void Read_TestMethods_MatchSyntaxReaderOrder()
    {
        // Arrange
        var source = Bookstore("tests/Bookstore.IntegrationTests/BooksApiTests.cs");

        // Act
        var names = Reader.Read(source).Select(f => f.TestMethod);

        // Assert
        names.Should().Equal(new CSharpTestSyntaxReader().Read(source).TestMethods);
    }

    [Fact]
    public void Read_BookstoreFixture_LevelsMatchD27()
    {
        // Arrange
        var files = new[]
        {
            "tests/Bookstore.IntegrationTests/BooksApiTests.cs",
            "tests/Bookstore.IntegrationTests/OrdersApiTests.cs",
            "tests/Bookstore.IntegrationTests/DbFixture.cs",
        };

        // Act
        var levels = files.SelectMany(f => Reader.Read(Bookstore(f))).Select(f => new { f.TestMethod, f.Level, f.ReadBack, f.ErrorPath });

        // Assert
        levels.Should().Equal(
            new { TestMethod = "BooksApiTests.List_returns_seeded_books", Level = (int?)2, ReadBack = (ReadBack?)null, ErrorPath = false },
            new { TestMethod = "BooksApiTests.Get_by_id_returns_ok", Level = (int?)1, ReadBack = (ReadBack?)null, ErrorPath = false },
            new { TestMethod = "BooksApiTests.Create_persists_book", Level = (int?)4, ReadBack = (ReadBack?)ReadBack.Api, ErrorPath = false },
            new { TestMethod = "BooksApiTests.Create_rejects_missing_title", Level = (int?)1, ReadBack = (ReadBack?)null, ErrorPath = true },
            new { TestMethod = "OrdersApiTests.Place_order_persists_lines_and_total", Level = (int?)3, ReadBack = (ReadBack?)null, ErrorPath = false },
            new { TestMethod = "DbFixture.Container_starts", Level = (int?)0, ReadBack = (ReadBack?)null, ErrorPath = false });
    }

    [Fact]
    public void Read_AnonymousPayload_CollectsSentFields()
    {
        // Arrange
        const string body = """
            var response = await _client.PostAsJsonAsync("api/books", new { title = "Dune", price = 9.5m });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.SentFields.Should().Equal("title", "price");
    }

    [Fact]
    public void Read_NestedCollectionPayload_UsesBracketPaths()
    {
        // Arrange
        var source = Bookstore("tests/Bookstore.IntegrationTests/OrdersApiTests.cs");

        // Act
        var facts = Reader.Read(source).Single();

        // Assert
        facts.SentFields.Should().Equal("lines[].bookId", "lines[].quantity");
    }

    [Fact]
    public void Read_PayloadFromBuilder_IsUnresolved()
    {
        // Arrange
        const string body = """
            var response = await _client.PostAsJsonAsync("api/books", BookBuilder.Valid());
            var created = await response.Content.ReadFromJsonAsync<Book>();
            Assert.Equal("Dune", created!.Title);
            """;

        // Act
        var facts = Single(body);

        // Assert
        new { facts.FieldsResolved, facts.SentFields, facts.CheckedFields }
            .Should().BeEquivalentTo(new { FieldsResolved = false, SentFields = Array.Empty<string>(), CheckedFields = Array.Empty<string>() });
    }

    [Fact]
    public void Read_PayloadThroughLocal_IsResolved()
    {
        // Arrange
        const string body = """
            var request = new { title = "Dune" };
            var response = await _client.PostAsJsonAsync("api/books", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.SentFields.Should().Equal("title");
    }

    [Fact]
    public void Read_CheckedFields_SkipCountAndLength()
    {
        // Arrange
        var source = Bookstore("tests/Bookstore.IntegrationTests/OrdersApiTests.cs");

        // Act
        var facts = Reader.Read(source).Single();

        // Assert
        facts.CheckedFields.Should().Equal("Lines", "Total");
    }

    [Fact]
    public void Read_EquivalentToRequest_ChecksAllSentFields()
    {
        // Arrange
        const string body = """
            var request = new { title = "Dune", price = 9.5m };
            var response = await _client.PostAsJsonAsync("api/books", request);
            var created = await response.Content.ReadFromJsonAsync<Book>();
            created.Should().BeEquivalentTo(request);
            """;

        // Act
        var facts = Single(body);

        // Assert
        facts.CheckedFields.Should().Contain(["title", "price"]);
    }

    [Fact]
    public void Read_BookstoreCreatePersistsBook_ChecksTitle()
    {
        // Arrange
        var source = Bookstore("tests/Bookstore.IntegrationTests/BooksApiTests.cs");

        // Act
        var facts = Reader.Read(source).Single(f => f.TestMethod == "BooksApiTests.Create_persists_book");

        // Assert
        new { facts.SentFields, facts.CheckedFields }.Should().BeEquivalentTo(new { SentFields = new[] { "title" }, CheckedFields = new[] { "Title" } });
    }
}
