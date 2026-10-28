using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>Only for EF migration authoring; never contains checked-in passwords.</summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ICEHOTT_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "Set ICEHOTT_MIGRATION_CONNECTION locally before generating migrations.");
        return new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
    }
}
