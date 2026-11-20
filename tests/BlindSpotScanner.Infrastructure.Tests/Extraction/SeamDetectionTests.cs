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

    // Value: protects=each supported technology registration is reported as its seam kind and name; fails_when=a table entry or branch is dropped or renamed; why_new=the facts above cover one registration per kind, not every supported one; seam=none; no_claim=real startup files (synthetic one-line sources)
    [Theory]
    [InlineData("services.AddDbContextPool<BookstoreDbContext>(o => o.UseNpgsql(cs));", SeamKind.Database, "bookstore-db")]
    [InlineData("services.AddDbContextFactory<BookstoreDbContext>(o => o.UseNpgsql(cs));", SeamKind.Database, "bookstore-db")]
    [InlineData("var factory = new ConnectionFactory { HostName = host };", SeamKind.MessageBroker, "rabbitmq")]
    [InlineData("var consumer = new ConsumerBuilder<string, string>(config).Build();", SeamKind.MessageBroker, "kafka")]
    [InlineData("var client = new ServiceBusClient(cs);", SeamKind.MessageBroker, "azure-service-bus")]
    [InlineData("var client = new AmazonS3Client(region);", SeamKind.BlobStorage, "s3")]
    [InlineData("var mux = ConnectionMultiplexer.Connect(cs);", SeamKind.Cache, "redis")]
    [InlineData("var mux = await ConnectionMultiplexer.ConnectAsync(cs);", SeamKind.Cache, "redis")]
    [InlineData("services.AddSingleton(new Azure.Storage.Blobs.BlobServiceClient(cs));", SeamKind.BlobStorage, "azure-blob-storage")]
    [InlineData("services.AddHttpClient<Billing.HTTPGatewayClient>();", SeamKind.OutboundHttp, "http-gateway")]
    public void DetectSeams_SupportedRegistration_ReturnsItsSeam(string source, SeamKind kind, string name)
    {
        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().Equal(new ProductionSeam(kind, name, Path));
    }

    // Value: protects=a registration that names no technology (no type argument, a non-literal name, another receiver) is not reported as a seam; fails_when=the guards are loosened so any Add*/Connect call becomes a seam; why_new=only the commented-out case checks for false positives; seam=none; no_claim=every possible non-seam call
    [Theory]
    [InlineData("services.AddHttpClient();")]
    [InlineData("services.AddHttpClient(name, c => c.BaseAddress = uri);")]
    [InlineData("services.AddDbContext(o => o.UseNpgsql(cs));")]
    [InlineData("services.AddAWSService<IAmazonSQS>();")]
    [InlineData("var socket = Socket.Connect(host);")]
    [InlineData("var seam = new Dictionary<string, string>();")]
    public void DetectSeams_CallWithoutTechnology_ReturnsNone(string source)
    {
        // Act
        var seams = Detect(source);

        // Assert
        seams.Should().BeEmpty();
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
