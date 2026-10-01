using Microsoft.EntityFrameworkCore;
using Npgsql;
using RPAS.Governance.Persistence.Data;

namespace RPAS.Governance.Tests;

/// <summary>
/// Real-PostgreSQL tests run only when RPAS_TEST_POSTGRES holds an ADMIN connection string, e.g.
/// "Host=localhost;Username=rpas;Password=...;Database=postgres". Each test class gets its own throwaway database,
/// created by running the real EF migrations, and dropped afterwards.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string EnvVar = "RPAS_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"Set {EnvVar} to run the PostgreSQL tests.";
        }
    }
}

public sealed class PostgresDatabase : IDisposable
{
    private readonly string? _adminConnectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvVar);
    private readonly string _name = "rpas_t_" + Guid.NewGuid().ToString("N");

    public string ConnectionString { get; private set; } = string.Empty;

    public PostgresDatabase()
    {
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            return; // tests are skipped
        }

        Admin($"CREATE DATABASE \"{_name}\"");
        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _name, Pooling = false }.ConnectionString;

        using var db = NewContext();
        db.Database.Migrate(); // the REAL migrations, including the append-only triggers
    }

    public GovernanceDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GovernanceDbContext>().UseNpgsql(ConnectionString).Options);

    public void Sql(string sql)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public long Scalar(string sql)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        try { Admin($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)"); } catch { /* best effort */ }
    }

    private void Admin(string sql)
    {
        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
