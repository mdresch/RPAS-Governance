using RPAS.Governance.Core.Models.Governance;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>AMD-2026-10-01-0001: single-use tokens are consumed atomically.</summary>
public sealed class TokenConsumptionTests : IDisposable
{
    private readonly string _path = TestDatabase.NewPath();

    public TokenConsumptionTests() => TestDatabase.Create(_path);

    public void Dispose() => TestDatabase.Delete(_path);

    private Guid Seed(int ttlSeconds = 120)
    {
        using var db = TestDatabase.NewContext(_path);
        var token = new AuthorityToken("MarkApproved", "entity-1", ttlSeconds, "petitioner-a");
        db.AuthorityTokens.Add(token);
        db.SaveChanges();
        return token.Id;
    }

    [Fact]
    public async Task ParallelConsumers_ExactlyOneWins()
    {
        const int contenders = 32;
        var id = Seed();
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            using var db = TestDatabase.NewContext(_path); // each contender has its own context and connection
            return await AuthorityTokenStore.TryConsumeAsync(db, id, DateTime.UtcNow);
        })).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r == TokenConsumeResult.Consumed));
        Assert.Equal(contenders - 1, results.Count(r => r == TokenConsumeResult.AlreadyConsumed));
    }

    [Fact]
    public async Task SecondConsumption_IsReportedAsReplay()
    {
        var id = Seed();
        using var db = TestDatabase.NewContext(_path);

        Assert.Equal(TokenConsumeResult.Consumed, await AuthorityTokenStore.TryConsumeAsync(db, id, DateTime.UtcNow));
        Assert.Equal(TokenConsumeResult.AlreadyConsumed, await AuthorityTokenStore.TryConsumeAsync(db, id, DateTime.UtcNow));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected_AndNotMarkedConsumed()
    {
        var id = Seed(ttlSeconds: -5);
        using var db = TestDatabase.NewContext(_path);

        Assert.Equal(TokenConsumeResult.Expired, await AuthorityTokenStore.TryConsumeAsync(db, id, DateTime.UtcNow));

        using var verify = TestDatabase.NewContext(_path);
        Assert.False(verify.AuthorityTokens.Single(t => t.Id == id).IsConsumed);
    }

    [Fact]
    public async Task UnknownToken_IsNotFound()
    {
        using var db = TestDatabase.NewContext(_path);
        Assert.Equal(TokenConsumeResult.NotFound, await AuthorityTokenStore.TryConsumeAsync(db, Guid.NewGuid(), DateTime.UtcNow));
    }
}
