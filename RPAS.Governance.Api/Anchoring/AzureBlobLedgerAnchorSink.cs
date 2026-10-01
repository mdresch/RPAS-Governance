using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Anchoring;

/// <summary>
/// Writes anchors as create-only blobs (If-None-Match: *). The container must have a time-based retention
/// (immutability) policy enabled: that policy, not this code, is what makes the anchors tamper-proof.
/// Authentication is by Azure identity (managed identity or developer sign-in); no keys are stored.
/// </summary>
public sealed class AzureBlobLedgerAnchorSink(BlobContainerClient container) : ILedgerAnchorSink
{
    private const string Prefix = "anchors/";

    public string Name => "azure-blob";

    public async Task WriteAsync(LedgerAnchor anchor, CancellationToken ct = default)
    {
        var blob = container.GetBlobClient(Prefix + AnchorNaming.FileName(anchor));
        var content = BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(anchor));

        try
        {
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            var existing = JsonSerializer.Deserialize<LedgerAnchor>((await blob.DownloadContentAsync(ct)).Value.Content.ToArray());
            if (existing is null || existing.Sequence != anchor.Sequence || existing.EntryHash != anchor.EntryHash)
            {
                throw new InvalidOperationException($"An anchor for sequence {anchor.Sequence} already exists with different content.");
            }
        }
    }

    public async Task<IReadOnlyList<LedgerAnchor>> ReadAllAsync(CancellationToken ct = default)
    {
        var anchors = new List<LedgerAnchor>();
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, Prefix, ct))
        {
            var data = await container.GetBlobClient(item.Name).DownloadContentAsync(ct);
            var anchor = JsonSerializer.Deserialize<LedgerAnchor>(data.Value.Content.ToArray());
            if (anchor is not null)
            {
                anchors.Add(anchor);
            }
        }
        return anchors;
    }
}
