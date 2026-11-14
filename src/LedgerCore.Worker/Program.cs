using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Outbox;
using LedgerCore.Worker;
using Serilog;
using Serilog.Formatting.Compact;

// The Worker is deployed separately from the API (spec §4): it can be restarted, scaled or
// paused without touching request handling, and a slow handler never slows a request down.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException("Connection string 'Ledger' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.Configure<IdempotencyCleanupOptions>(builder.Configuration.GetSection(IdempotencyCleanupOptions.SectionName));

builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<IdempotencyCleanupWorker>();

await builder.Build().RunAsync();
