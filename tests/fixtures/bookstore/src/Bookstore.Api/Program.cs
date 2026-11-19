var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<BookstoreDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Bookstore")));
builder.Services.AddHttpClient<PaymentGatewayClient>();

var app = builder.Build();
app.MapControllers();
app.Run();

public partial class Program;
