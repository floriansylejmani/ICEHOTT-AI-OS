using ICEHOTT.Domain;
using ICEHOTT.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ICEHOTT.Tests.Integration;

[Collection("postgres-tenancy")]
public sealed class WorkspaceAccessPostgresTests(TenantDatabaseFixture database)
{
    [Fact]
    public async Task CreatorAtomicallyBecomesOwner_AndOtherUserHasNoAccess()
    {
        var owner = await database.CreateUserAsync();
        var outsider = await database.CreateUserAsync();
        await using var db = database.OpenContext();
        var result = await new WorkspaceAccessService(db)
            .CreateAsync(owner.Id, "workspace-" + Guid.NewGuid().ToString("N")[..12], "  Example Team  ");

        Assert.Equal("Example Team", result.DisplayName);
        Assert.Equal(WorkspaceRole.Owner, result.Role);

        await using var read = database.OpenContext();
        var service = new WorkspaceAccessService(read);
        Assert.True(await service.HasRoleAsync(owner.Id, result.Id, WorkspaceRole.Owner));
        Assert.True(await service.HasRoleAsync(owner.Id, result.Id, WorkspaceRole.Viewer));
        Assert.False(await service.HasRoleAsync(outsider.Id, result.Id, WorkspaceRole.Viewer));
        Assert.False(await service.HasRoleAsync(outsider.Id, result.Id, WorkspaceRole.Owner));
        Assert.Single(await service.ListMineAsync(owner.Id));
        Assert.Empty(await service.ListMineAsync(outsider.Id));
    }

    [Fact]
    public async Task UnknownUserAndEmptyActorCannotCreateOrListWorkspace()
    {
        await using var db = database.OpenContext();
        var service = new WorkspaceAccessService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CreateAsync(Guid.Empty, "forbidden-workspace", "No"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CreateAsync(Guid.NewGuid(), "unknown-workspace", "No"));
        Assert.Empty(await service.ListMineAsync(Guid.Empty));
        Assert.False(await service.HasRoleAsync(Guid.Empty, Guid.NewGuid(), WorkspaceRole.Viewer));
        Assert.False(await service.HasRoleAsync(Guid.NewGuid(), Guid.Empty, WorkspaceRole.Viewer));
        Assert.False(await service.HasRoleAsync(Guid.NewGuid(), Guid.NewGuid(), (WorkspaceRole)99));
    }

    [Fact]
    public async Task InvalidSlugsAndDisplayNamesRejectedWithoutDatabaseWrite()
    {
        var user = await database.CreateUserAsync();
        await using var db = database.OpenContext();
        var service = new WorkspaceAccessService(db);
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(user.Id, "../other-tenant", "bad"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(user.Id, "Valid-Mixed-Case", "bad"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(user.Id, "good-slug", "  "));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(user.Id, "good-slug", new string('x', 121)));
        Assert.Empty(await service.ListMineAsync(user.Id));
    }

    [Fact]
    public async Task DatabaseEnforcesUniqueSlugAcrossUsers()
    {
        var first = await database.CreateUserAsync();
        var second = await database.CreateUserAsync();
        var slug = "unique-" + Guid.NewGuid().ToString("N")[..16];
        await using (var db = database.OpenContext())
            await new WorkspaceAccessService(db).CreateAsync(first.Id, slug, "First");
        await using (var db = database.OpenContext())
            await Assert.ThrowsAsync<WorkspaceSlugAlreadyTakenException>(
                () => new WorkspaceAccessService(db).CreateAsync(second.Id, slug, "Second"));

        await using var read = database.OpenContext();
        Assert.Single(await new WorkspaceAccessService(read).ListMineAsync(first.Id));
        Assert.Empty(await new WorkspaceAccessService(read).ListMineAsync(second.Id));
    }

    [Fact]
    public async Task ForeignKeysAndRoleChecksPreventInvalidMembershipRows()
    {
        var user = await database.CreateUserAsync();
        await using var db = database.OpenContext();
        db.Memberships.Add(new WorkspaceMembership
        {
            WorkspaceId = Guid.NewGuid(),
            UserId = user.Id,
            Role = WorkspaceRole.Owner,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,
            Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task MemberSeesOnlyItsWorkspacesAndCannotActAsAdmin()
    {
        var owner = await database.CreateUserAsync();
        var member = await database.CreateUserAsync();
        var other = await database.CreateUserAsync();
        var slug = "member-" + Guid.NewGuid().ToString("N")[..12];

        WorkspaceSummary workspace;
        await using (var db = database.OpenContext())
            workspace = await new WorkspaceAccessService(db).CreateAsync(owner.Id, slug, "Members");
        await using (var db = database.OpenContext())
        {
            db.Memberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id, UserId = member.Id, Role = WorkspaceRole.Member,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var read = database.OpenContext();
        var service = new WorkspaceAccessService(read);
        Assert.True(await service.HasRoleAsync(member.Id, workspace.Id, WorkspaceRole.Viewer));
        Assert.True(await service.HasRoleAsync(member.Id, workspace.Id, WorkspaceRole.Member));
        Assert.False(await service.HasRoleAsync(member.Id, workspace.Id, WorkspaceRole.Admin));
        Assert.False(await service.HasRoleAsync(member.Id, workspace.Id, WorkspaceRole.Owner));
        Assert.False(await service.HasRoleAsync(other.Id, workspace.Id, WorkspaceRole.Viewer));
        Assert.Equal(WorkspaceRole.Member, Assert.Single(await service.ListMineAsync(member.Id)).Role);
        Assert.Empty(await service.ListMineAsync(other.Id));
    }

    [Fact]
    public async Task DatabaseRejectsInvalidRoleAndNoncanonicalSlug()
    {
        var user = await database.CreateUserAsync();
        var invited = await database.CreateUserAsync();
        await using (var db = database.OpenContext())
        {
            db.Workspaces.Add(new WorkspaceRecord
            {
                Id = Guid.NewGuid(), Slug = "UPPER-CASE",
                DisplayName = "Invalid", CreatedAtUtc = DateTimeOffset.UtcNow
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
        await using (var db = database.OpenContext())
        {
            var workspace = await new WorkspaceAccessService(db)
                .CreateAsync(user.Id, "role-" + Guid.NewGuid().ToString("N")[..12], "Roles");
            await using var separate = database.OpenContext();
            separate.Memberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id, UserId = invited.Id, Role = (WorkspaceRole)97,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => separate.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }
    }
    [Fact]
    public async Task ConcurrentSameSlugCreatesExactlyOneOwnerMembership()
    {
        var a = await database.CreateUserAsync();
        var b = await database.CreateUserAsync();
        var slug = "race-" + Guid.NewGuid().ToString("N")[..16];

        async Task<bool> AttemptAsync(Guid userId)
        {
            await using var db = database.OpenContext();
            try
            {
                await new WorkspaceAccessService(db).CreateAsync(userId, slug, "Concurrent");
                return true;
            }
            catch (WorkspaceSlugAlreadyTakenException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(AttemptAsync(a.Id), AttemptAsync(b.Id));
        Assert.Single(results, x => x);

        await using var read = database.OpenContext();
        var workspace = await read.Workspaces.SingleAsync(w => w.Slug == slug);
        var membership = await read.Memberships.SingleAsync(m => m.WorkspaceId == workspace.Id);
        Assert.Equal(WorkspaceRole.Owner, membership.Role);
        Assert.True(membership.UserId == a.Id || membership.UserId == b.Id);
    }

}
