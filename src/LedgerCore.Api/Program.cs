using LedgerCore.Api.Authentication;
using LedgerCore.Api.ErrorHandling;
using LedgerCore.Application;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

// Never in appsettings.json: comes from user secrets locally and the environment in containers.
var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException("Connection string 'Ledger' is not configured.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.Services.InitializeDatabaseAsync();

await app.RunAsync();

// Exposed so integration tests can host the API via WebApplicationFactory<Program>.
public partial class Program;
