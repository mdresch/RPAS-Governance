using System.Text.Json;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Anchoring;

/// <summary>
/// Writes anchors as create-only, read-only files. Meant for development and for directories that sit on
/// write-once storage (WORM volume, immutable mount). A plain local directory is NOT tamper-proof against
/// someone with filesystem access; production should use the Azure Blob sink with an immutability policy.
/// </summary>
public sealed class FileLedgerAnchorSink(string directory) : ILedgerAnchorSink
{
    public string Name => "file";

    public async Task WriteAsync(LedgerAnchor anchor, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AnchorNaming.FileName(anchor));
        var content = JsonSerializer.SerializeToUtf8Bytes(anchor);

        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                await stream.WriteAsync(content, ct);
            }
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Create-only: an existing anchor is never replaced. Identical content is idempotent; anything else is an error.
            var existing = JsonSerializer.Deserialize<LedgerAnchor>(await File.ReadAllBytesAsync(path, ct));
            if (existing is null || existing.Sequence != anchor.Sequence || existing.EntryHash != anchor.EntryHash)
            {
                throw new InvalidOperationException($"An anchor for sequence {anchor.Sequence} already exists with different content.");
            }
        }
    }

    public async Task<IReadOnlyList<LedgerAnchor>> ReadAllAsync(CancellationToken ct = default)
    {
        var anchors = new List<LedgerAnchor>();
        if (!Directory.Exists(directory))
        {
            return anchors;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var anchor = JsonSerializer.Deserialize<LedgerAnchor>(await File.ReadAllBytesAsync(file, ct));
            if (anchor is not null)
            {
                anchors.Add(anchor);
            }
        }
        return anchors;
    }
}

internal static class AnchorNaming
{
    public static string FileName(LedgerAnchor a) => $"{a.Sequence:D12}-{a.EntryHash}.json";
}
