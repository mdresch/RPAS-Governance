using System;

namespace RPAS.Governance.Client.DTOs;

/// <summary>
/// A cryptographically short-lived authority token issued by the Sovereign Courthouse.
/// </summary>
public record AuthorityTokenDto(
    Guid Id,
    DateTime ExpiresAt,
    string RitualType
);

/// <summary>
/// The result of a sovereign ritual petition.
/// </summary>
public record GovernanceResult(
    bool Success,
    string Message,
    AuthorityTokenDto? AuthorityToken = null
);
