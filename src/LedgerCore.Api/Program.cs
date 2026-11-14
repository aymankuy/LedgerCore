using LedgerCore.Api.Authentication;
using LedgerCore.Api.ErrorHandling;
using LedgerCore.Api.Idempotency;
using LedgerCore.Api.Logging;
using LedgerCore.Api.OpenApi;
using LedgerCore.Application;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence.Seeding;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON to stdout: containers and App Service collect it from there.
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

// Never in appsettings.json: comes from user secrets locally and the environment in containers.
var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException("Connection string 'Ledger' is not configured.");

// Before AddApplication, which otherwise registers a schedule without fees.
var feeRules = builder.Configuration.GetSection("Fees").Get<Dictionary<string, FeeRule>>() ?? [];
builder.Services.AddSingleton(new FeeSchedule(feeRules));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddScoped<IIdempotencyContext, HttpIdempotencyContext>();

builder.Services.AddControllers();
builder.Services.AddLedgerSwagger();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseLedgerSwagger();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.Services.InitializeDatabaseAsync();

await app.RunAsync();

// Exposed so integration tests can host the API via WebApplicationFactory<Program>.
public partial class Program;
