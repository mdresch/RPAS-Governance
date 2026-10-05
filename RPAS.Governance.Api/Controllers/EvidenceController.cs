using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RPAS.Governance.Api.Security;
using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Api.Controllers;

public class EvidenceRecordRequest
{
    public string? RitualType { get; set; }
    public string? EntityId { get; set; }
    public string? ContentHash { get; set; }
    public string? HashAlgorithm { get; set; }
    public string? KeyId { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

/// <summary>
/// Hash-only evidence (AMD-2026-10-01-0006): the petitioner keeps the authoritative record in its own
/// access-controlled system and sends only a hash plus allow-listed metadata. The hash is chained into the ledger.
/// </summary>
[ApiController]
[Route("[controller]")]
public class EvidenceController(GovernanceDbContext db, ILogger<EvidenceController> logger) : ControllerBase
{
    [HttpPost("record")]
    public async Task<IActionResult> Record([FromBody] EvidenceRecordRequest request, CancellationToken ct)
    {
        var petitionerId = RpasAuthentication.GetPetitionerId(User);
        if (petitionerId is null)
        {
            return StatusCode(403, new { error = "Authenticated caller carries no petitioner identity." });
        }

        var metadata = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (request.Metadata is not null)
        {
            foreach (var (key, element) in request.Metadata)
            {
                metadata[key] = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                    _ => null // objects, arrays, null, fractions: rejected by the policy below
                };
            }
        }

        // AMD-2026-10-01-0007: the ritual and its allowed metadata keys come from the versioned definition in force
        // for this petitioner, not from a static list. A retired or non-evidence ritual is simply not allowed.
        var definition = await RitualDefinitionStore.ResolveAsync(db, petitionerId, request.RitualType, ct);
        var allowedKeys = definition is { AcceptsEvidence: true } ? definition.MetadataKeys : null;

        var problem = HashOnlyPolicy.Validate(
            request.RitualType, allowedKeys, request.EntityId, request.ContentHash, request.HashAlgorithm, request.KeyId, metadata);
        if (problem is not null)
        {
            return BadRequest(new { error = problem });
        }

        var entry = GovernanceLedgerEntry.CreateHashOnly(request.RitualType!, petitionerId, BuildProof(request, metadata));

        try
        {
            db.GovernanceLedgerEntries.Add(entry);
            await LedgerChain.SaveChangesAsync(db, ct);
        }
        catch (LedgerContentionException)
        {
            Response.Headers.RetryAfter = "1";
            return StatusCode(503, new { error = "Ledger is busy; retry the request." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record hash-only evidence for ritual {Ritual}.", entry.RitualType);
            return StatusCode(500, new { error = "An internal error occurred while recording evidence." });
        }

        logger.LogInformation("Recorded hash-only evidence {Ritual} at ledger sequence {Sequence}.", entry.RitualType, entry.Sequence);

        return Created($"/Ledger/entries/{entry.Id}", new
        {
            id = entry.Id,
            sequence = entry.Sequence,
            entryHash = entry.EntryHash,
            prevHash = entry.PrevHash
        });
    }

    /// <summary>Deterministic JSON (fixed property order, sorted metadata) so the stored proof is stable.</summary>
    private static string BuildProof(EvidenceRecordRequest request, SortedDictionary<string, object?> metadata)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("entityId", request.EntityId);
            w.WriteString("contentHash", request.ContentHash);
            w.WriteString("hashAlgorithm", request.HashAlgorithm);
            if (request.KeyId is not null)
            {
                w.WriteString("keyId", request.KeyId);
            }
            w.WriteStartObject("metadata");
            foreach (var (key, value) in metadata)
            {
                switch (value)
                {
                    case string s: w.WriteString(key, s); break;
                    case bool b: w.WriteBoolean(key, b); break;
                    case int i: w.WriteNumber(key, i); break;
                }
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
