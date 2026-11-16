using System.Text.Json;
using Azure.Storage.Blobs;
using FluentAssertions;
using LedgerCore.Infrastructure.Reporting;
using Testcontainers.Azurite;

namespace LedgerCore.IntegrationTests.Reporting;

/// <summary>Writes reports to Azurite, the Azure Storage emulator, through the real Blob SDK.</summary>
public sealed class ReconciliationReportStoreTests : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private BlobContainerClient _container = null!;

    public async Task InitializeAsync()
    {
        await _azurite.StartAsync();
        _container = new BlobContainerClient(_azurite.GetConnectionString(), "reconciliation");
        await _container.CreateAsync();
    }

    public Task DisposeAsync() => _azurite.DisposeAsync().AsTask();

    [Fact]
    public async Task A_report_is_stored_as_json_under_its_date_and_a_rerun_replaces_it()
    {
        var store = new ReconciliationReportStore(_container);
        var day = new DateOnly(2026, 6, 10);

        var name = await store.SaveAsync(Report(day, unbalanced: 3), CancellationToken.None);
        await store.SaveAsync(Report(day, unbalanced: 0), CancellationToken.None);

        name.Should().Be("2026/06/10.json");
        var blobs = new List<string>();
        await foreach (var blob in _container.GetBlobsAsync())
        {
            blobs.Add(blob.Name);
        }

        blobs.Should().Equal("2026/06/10.json");

        var content = await _container.GetBlobClient(name).DownloadContentAsync();
        content.Value.Details.ContentType.Should().Be("application/json");
        var json = JsonDocument.Parse(content.Value.Content.ToString()).RootElement;
        json.GetProperty("isBalanced").GetBoolean().Should().BeTrue("the second run replaced the first");
        json.GetProperty("report").GetProperty("day").GetString().Should().Be("2026-06-10");
        json.GetProperty("report").GetProperty("unbalancedTransactionCount").GetInt64().Should().Be(0);
    }

    private static ReconciliationReport Report(DateOnly day, long unbalanced) => new(
        day,
        DateTime.UtcNow,
        [new CurrencyActivity("EUR", 1, 2, 10m, 10m)],
        unbalanced,
        [],
        0,
        [],
        [new CurrencyNet("EUR", 0m)]);
}
