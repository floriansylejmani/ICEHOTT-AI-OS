using ICEHOTT.Domain;
using ICEHOTT.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ICEHOTT.Tests.Integration;

[CollectionDefinition("postgres-tenancy")]
public sealed class PostgresTenancyCollection : ICollectionFixture<TenantDatabaseFixture>;

/// <summary>Creates and drops an isolated PostgreSQL database per test run.
/// The CI job must set ICEHOTT_POSTGRES_TEST_CONNECTION; tests do not silently skip.</summary>
public sealed class TenantDatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = "icehott_v2_tenancy_" + Guid.NewGuid().ToString("N");
    private string _connectionString = "";
    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        var baseline = Environment.GetEnvironmentVariable("ICEHOTT_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(baseline))
            throw new InvalidOperationException(
                "ICEHOTT_POSTGRES_TEST_CONNECTION required; tests must run against real PostgreSQL.");

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseline) { Database = "postgres" };
        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"CREATE DATABASE \"{_databaseName}\"", admin);
            await command.ExecuteNonQueryAsync();
        }
        var dbBuilder = new NpgsqlConnectionStringBuilder(baseline) { Database = _databaseName };
        _connectionString = dbBuilder.ConnectionString;
        await using var db = OpenContext();
        await db.Database.MigrateAsync();
    }

    public IdentityDbContext OpenContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new IdentityDbContext(options);
    }

    public async Task<UserAccount> CreateUserAsync()
    {
        var id = Guid.NewGuid();
        var user = new UserAccount
        {
            Id = id,
            EmailNormalized = $"{id:N}@test.invalid",
            PasswordHash = "not-a-login-hash-fixture-only",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await using var db = OpenContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
            return;
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(_connectionString) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await cmd.ExecuteNonQueryAsync();
    }
}
