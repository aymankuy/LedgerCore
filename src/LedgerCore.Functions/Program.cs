using Azure.Identity;
using Azure.Storage.Blobs;
using LedgerCore.Infrastructure;
using LedgerCore.Infrastructure.Reporting;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Nightly jobs on Azure (ADR 0011): the reconciliation report, plus what the Worker does
// continuously in docker compose. A Function rather than Hangfire: it runs on its own schedule
// while the API sleeps, scales to zero in between, and costs nothing when idle.
var builder = FunctionsApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Ledger")
    ?? throw new InvalidOperationException("Connection string 'Ledger' is not configured.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddSingleton(TimeProvider.System);

// On Azure the container is reached with the app's managed identity, so no storage key exists.
// A connection string, for example to Azurite, is accepted for running locally.
builder.Services.AddSingleton(_ =>
{
    var reports = builder.Configuration.GetSection("Reports");
    return reports["ConnectionString"] is { Length: > 0 } local
        ? new BlobContainerClient(local, reports["ContainerName"] ?? "reconciliation")
        : new BlobContainerClient(
            new Uri(reports["ContainerUri"] ?? throw new InvalidOperationException("Reports:ContainerUri is not configured.")),
            new DefaultAzureCredential());
});
builder.Services.AddScoped<ReconciliationReportStore>();
builder.Services.AddScoped<DailyReconciliation>();

builder.Build().Run();
