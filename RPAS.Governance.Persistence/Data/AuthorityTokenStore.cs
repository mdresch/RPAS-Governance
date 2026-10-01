using Microsoft.EntityFrameworkCore;
using RPAS.Governance.Core.Models.Governance;

namespace RPAS.Governance.Persistence.Data;

public enum TokenConsumeResult
{
    Consumed,
    NotFound,
    AlreadyConsumed,
    Expired
}

/// <summary>
/// Single-use token consumption (AMD-2026-10-01-0001).
///
/// The previous flow read the token, checked IsConsumed in memory, then wrote it back, so two
/// parallel requests could both observe IsConsumed == false and both succeed. Consumption is now ONE
/// conditional UPDATE; the database decides the winner and the affected-row count is the verdict.
/// </summary>
public static class AuthorityTokenStore
{
    public static async Task<TokenConsumeResult> TryConsumeAsync(
        GovernanceDbContext db,
        Guid tokenId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.AuthorityTokens
            .Where(t => t.Id == tokenId && !t.IsConsumed && t.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsConsumed, true), cancellationToken);

        if (rows == 1)
        {
            return TokenConsumeResult.Consumed;
        }

        // The claim failed. Classify why, for the caller's response only; the decision is already made.
        var state = await db.AuthorityTokens
            .AsNoTracking()
            .Where(t => t.Id == tokenId)
            .Select(t => new { t.IsConsumed })
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return TokenConsumeResult.NotFound;
        }

        return state.IsConsumed ? TokenConsumeResult.AlreadyConsumed : TokenConsumeResult.Expired;
    }
}
