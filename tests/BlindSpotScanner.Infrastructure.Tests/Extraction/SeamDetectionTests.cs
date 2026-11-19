using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Extraction;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

public class SeamDetectionTests
{
    private const string Path = "src/Api/Program.cs";

    private static IReadOnlyList<ProductionSeam> Detect(string source) => new AspNetCoreEndpointExtractor().DetectSeams(Path, source);

    [Fact]
    public void DetectSeams_AddDbContext_ReturnsDatabaseSeam()
    {
        // Arrange
        const string source = "builder.Services.AddDbContext<BookstoreDbContext>(o => o.UseNpgsql(cs));";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.Database, "bookstore-db", Path));
    }

    [Fact]
    public void DetectSeams_TypedHttpClient_ReturnsOutboundHttpSeam()
    {
        // Arrange
        const string source = "builder.Services.AddHttpClient<PaymentGatewayClient>();";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.OutboundHttp, "payment-gateway", Path));
    }

    [Fact]
    public void DetectSeams_InterfaceAndImplementationClient_UsesImplementation()
    {
        // Arrange
        const string source = "services.AddHttpClient<IPayments, StripePaymentsClient>();";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.OutboundHttp, "stripe-payments", Path));
    }

    [Fact]
    public void DetectSeams_NamedHttpClient_UsesKebabName()
    {
        // Arrange
        const string source = """services.AddHttpClient("ShippingApi", c => c.BaseAddress = uri);""";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.OutboundHttp, "shipping-api", Path));
    }

    [Fact]
    public void DetectSeams_MassTransit_ReturnsBrokerSeam()
    {
        // Arrange
        const string source = "services.AddMassTransit(x => x.UsingRabbitMq());";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.MessageBroker, "masstransit", Path));
    }

    [Fact]
    public void DetectSeams_KafkaProducer_ReturnsBrokerSeam()
    {
        // Arrange
        const string source = "var producer = new ProducerBuilder<string, string>(config).Build();";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.MessageBroker, "kafka", Path));
    }

    [Fact]
    public void DetectSeams_BlobServiceClient_ReturnsBlobSeam()
    {
        // Arrange
        const string source = "services.AddSingleton(new BlobServiceClient(cs));";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.BlobStorage, "azure-blob-storage", Path));
    }

    [Fact]
    public void DetectSeams_AwsS3Service_ReturnsBlobSeam()
    {
        // Arrange
        const string source = "services.AddAWSService<IAmazonS3>();";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.BlobStorage, "s3", Path));
    }

    [Fact]
    public void DetectSeams_RedisCache_ReturnsCacheSeam()
    {
        // Arrange
        const string source = "services.AddStackExchangeRedisCache(o => o.Configuration = cs);";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.Cache, "redis", Path));
    }

    [Fact]
    public void DetectSeams_CommentedOutRegistration_ReturnsNone()
    {
        // Arrange
        const string source = "// services.AddHttpClient<PaymentGatewayClient>();";

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().BeEmpty();
    }

    [Fact]
    public void DetectSeams_SameClientTwice_ReturnsOneSeam()
    {
        // Arrange
        const string source = """
            services.AddHttpClient<PaymentGatewayClient>();
            services.AddHttpClient<PaymentGatewayClient>();
            """;

        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(SeamKind.OutboundHttp, "payment-gateway", Path));
    }

    [Fact]
    public void DetectSeams_SeveralSeams_AreOrderedByName()
    {
        // Arrange
        const string source = """
            services.AddHttpClient<PaymentGatewayClient>();
            services.AddDbContext<BookstoreDbContext>();
            """;

        // Act
        var seams = Detect(source);

        // Assert
        seams.Select(s => s.Name).Should().Equal("bookstore-db", "payment-gateway");
    }

    [Fact]
    public void DetectSeams_NonCSharpPath_ReturnsNone()
    {
        // Arrange
        var extractor = new AspNetCoreEndpointExtractor();

        // Act
        var seams = extractor.DetectSeams("src/server.ts", "services.AddHttpClient<PaymentGatewayClient>();");

        // Assert
        seams.Should().BeEmpty();
    }
}
