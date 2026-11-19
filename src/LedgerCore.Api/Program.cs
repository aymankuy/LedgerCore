using LedgerCore.Api.Authentication;
using LedgerCore.Api.ErrorHandling;
using LedgerCore.Api.Health;
using LedgerCore.Api.Idempotency;
using LedgerCore.Api.Logging;
using LedgerCore.Api.OpenApi;
using LedgerCore.Api.RateLimiting;
using LedgerCore.Application;
using LedgerCore.Application.Abstractions.Idempotency;
using LedgerCore.Application.Ledger.Fees;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
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

// Optional: without a Redis connection string every balance read goes to SQL Server.
if (builder.Configuration.GetConnectionString("Redis") is { Length: > 0 } redisConnectionString)
{
    builder.Services.AddRedisCache(redisConnectionString);
}

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddScoped<IIdempotencyContext, HttpIdempotencyContext>();
builder.Services.AddAuthRateLimit(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddLedgerSwagger();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Get(context.HttpContext));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Live: the process answers, with no dependency checked, so a database outage never gets a
// healthy instance restarted. App Service pings this one every minute. Ready: the database is
// reachable too. Pinging it every minute would keep a serverless database from ever pausing.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);

var app = builder.Build();

// Tells browsers to use HTTPS only for this host. It is skipped on plain HTTP and on localhost,
// so docker compose is unaffected; on App Service the request is HTTPS (forwarded headers).
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseLedgerSwagger();

// After routing has picked the endpoint, before authentication so a flood is turned away cheaply.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });

await app.Services.InitializeDatabaseAsync();

// A cloud deploy runs the API once with Database:MigrateOnly as an administrator, then
// starts the new version with no DDL rights at all.
if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnly)
{
    return;
}

await app.RunAsync();

// Exposed so integration tests can host the API via WebApplicationFactory<Program>.
public partial class Program
{
    private const string ReadyTag = "ready";
}
