using System;
using System.Collections.Generic;

namespace RPAS.Governance.Core.Models.Governance;

public class AuthorityToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    
    public string RitualType { get; init; } = string.Empty;
    
    public string EntityId { get; init; } = string.Empty;
    
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
    
    public DateTime ExpiresAt { get; init; }
    
    public bool IsConsumed { get; private set; } = false;
    
    public List<string> AllowedPaths { get; init; } = new();

    /// <summary>
    /// Authenticated identity of the petitioner the token was issued to (AMD-2026-10-01-0003).
    /// Only that petitioner may consume the token. Null only on rows issued before the amendment;
    /// such tokens are treated as unbound and are never honoured.
    /// </summary>
    public string? PetitionerId { get; init; }

    protected AuthorityToken() { }

    public AuthorityToken(string ritualType, string entityId, int ttlSeconds = 120, string? petitionerId = null)
    {
        RitualType = ritualType;
        EntityId = entityId;
        ExpiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds);
        PetitionerId = petitionerId;
    }

    public void MarkConsumed()
    {
        if (IsConsumed)
        {
            throw new InvalidOperationException("Authority token has already been consumed.");
        }
        
        if (DateTime.UtcNow > ExpiresAt)
        {
            throw new InvalidOperationException("Authority token has expired.");
        }
        
        IsConsumed = true;
    }
}
