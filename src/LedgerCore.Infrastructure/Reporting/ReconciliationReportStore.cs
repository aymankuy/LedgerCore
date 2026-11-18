using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace LedgerCore.Infrastructure.Reporting;

/// <summary>
/// Writes each report as JSON to <c>{yyyy}/{MM}/{dd}.json</c> in a blob container. Running the
/// same day again replaces that day's file, so a re-run never leaves two reports for one day.
/// </summary>
public sealed class ReconciliationReportStore(BlobContainerClient container)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Saves the report and returns the blob name it was written to.</summary>
    public async Task<string> SaveAsync(ReconciliationReport report, CancellationToken cancellationToken)
    {
        var name = $"{report.Day:yyyy}/{report.Day:MM}/{report.Day:dd}.json";

        // System.Text.Json writes the computed IsBalanced too, so the verdict is in the document.
        var content = BinaryData.FromObjectAsJson(report, Json);

        await container.GetBlobClient(name).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" } },
            cancellationToken);

        return name;
    }
}
