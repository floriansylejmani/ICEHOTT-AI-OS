using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICEHOTT.Domain;
using ICEHOTT.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ICEHOTT.Tests.Integration;

[Collection("postgres-tenancy")]
public sealed class AuthenticatedApiTests(TenantDatabaseFixture database)
{
    private TestApp Start(bool registration = true) => new(database.ConnectionString, registration);

    [Fact]
    public async Task AnonymousWorkspaceEndpointsAreDenied()
    {
        using var app = Start();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/workspaces/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/workspaces/", new { slug = "unauthorized", name = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/invitations/accept", new { token = "fake" })).StatusCode);
    }

    [Fact]
    public async Task RegistrationRequiresVerification_ThenLogin_Refresh_ReplayRevokesFamily_Logout()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        var password = "Correct Horse Battery 8832";

        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password))).StatusCode);
        var verify = app.Mail.GetToken(email, "verify-email");
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(verify))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(verify))).StatusCode);

        var login = await LoginAsync(client, email, password);
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokenPair>())!;
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(rotated.RefreshToken))).StatusCode);

        var fresh = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workspaces/")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/workspaces/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(fresh.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task PasswordResetRevokesExistingSessionsAndInvalidatesOneTimeToken()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        await CreateVerifiedAsync(app, client, email);
        var previous = await LoginAsync(client, email, Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", previous.AccessToken);

        var request = await client.PostAsJsonAsync("/api/auth/request-password-reset", new { email });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = app.Mail.GetToken(email, "reset-password");
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/reset-password",
                new PasswordResetConfirm(token, "New Long Password 789123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/reset-password",
                new PasswordResetConfirm(token, "Another Password 789123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/workspaces/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password))).StatusCode);
        var updated = await LoginAsync(client, email, "New Long Password 789123");
        Assert.False(string.IsNullOrEmpty(updated.AccessToken));
    }

    [Fact]
    public async Task WorkspaceApiDeniesCrossTenantAccessAndSupportsInvitesAndOwnerTransfer()
    {
        using var app = Start();
        using var ownerClient = app.CreateClient();
        using var memberClient = app.CreateClient();
        using var outsiderClient = app.CreateClient();
        var ownerEmail = UniqueEmail();
        var memberEmail = UniqueEmail();
        var outsiderEmail = UniqueEmail();
        await CreateVerifiedAsync(app, ownerClient, ownerEmail);
        await CreateVerifiedAsync(app, memberClient, memberEmail);
        await CreateVerifiedAsync(app, outsiderClient, outsiderEmail);
        SetBearer(ownerClient, await LoginAsync(ownerClient, ownerEmail, Password));
        SetBearer(memberClient, await LoginAsync(memberClient, memberEmail, Password));
        SetBearer(outsiderClient, await LoginAsync(outsiderClient, outsiderEmail, Password));

        var create = await ownerClient.PostAsJsonAsync("/api/workspaces/", new
        {
            slug = "owner-" + Guid.NewGuid().ToString("N")[..12], name = "Test Workspace"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var workspace = (await create.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        Assert.Empty((await memberClient.GetFromJsonAsync<List<WorkspaceSummary>>("/api/workspaces/"))!);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsiderClient.GetAsync($"/api/workspaces/{workspace.Id}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/invitations",
                new InviteWorkspaceRequest(ownerEmail, WorkspaceRole.Admin))).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted,
            (await ownerClient.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/invitations",
                new InviteWorkspaceRequest(memberEmail, WorkspaceRole.Member))).StatusCode);
        var invite = app.Mail.GetToken(memberEmail, "accept-invite");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await outsiderClient.PostAsJsonAsync("/api/invitations/accept", new AcceptInviteRequest(invite))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await memberClient.PostAsJsonAsync("/api/invitations/accept", new AcceptInviteRequest(invite))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await memberClient.PostAsJsonAsync("/api/invitations/accept", new AcceptInviteRequest(invite))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.GetAsync($"/api/workspaces/{workspace.Id}/invitations")).StatusCode);
        var members = (await ownerClient.GetFromJsonAsync<List<MemberSummary>>($"/api/workspaces/{workspace.Id}/members"))!;
        Assert.Equal(2, members.Count);
        var invited = members.Single(x => x.Email == memberEmail);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.PutAsJsonAsync($"/api/workspaces/{workspace.Id}/members/{invited.UserId}/role",
                new ChangeRoleRequest(WorkspaceRole.Admin))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{members.Single(x => x.Email == ownerEmail).UserId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.PostAsync($"/api/workspaces/{workspace.Id}/transfer-ownership/{invited.UserId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ownerClient.PostAsync($"/api/workspaces/{workspace.Id}/transfer-ownership/{invited.UserId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ownerClient.PutAsJsonAsync($"/api/workspaces/{workspace.Id}/members/{invited.UserId}/role",
                new ChangeRoleRequest(WorkspaceRole.Viewer))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsiderClient.GetAsync($"/api/workspaces/{workspace.Id}/members")).StatusCode);
    }

    [Fact]
    public async Task RegistrationDisabledByDefaultPolicy_ResistsCredentialEnumeration()
    {
        using var disabled = Start(registration: false);
        using var client = disabled.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(UniqueEmail(), Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/auth/request-password-reset",
                new { email = UniqueEmail() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(UniqueEmail(), Password))).StatusCode);
    }


    [Fact]
    public async Task ParallelRefreshHasOneWinner_AndReplayRevokesTheNewSession()
    {
        using var app = Start();
        using var firstClient = app.CreateClient();
        using var secondClient = app.CreateClient();
        var email = UniqueEmail();
        await CreateVerifiedAsync(app, firstClient, email);
        var initial = await LoginAsync(firstClient, email, Password);

        var calls = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(initial.RefreshToken)),
            secondClient.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(initial.RefreshToken)));
        Assert.Single(calls, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(calls, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var success = calls.Single(x => x.StatusCode == HttpStatusCode.OK);
        var rotated = (await success.Content.ReadFromJsonAsync<TokenPair>())!;
        SetBearer(secondClient, rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/api/workspaces/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await firstClient.PostAsJsonAsync("/api/auth/refresh",
                new RefreshRequest(rotated.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task InvitationCanBeRevoked_OtherTenantCannotAccessOrUseToken()
    {
        using var app = Start();
        using var ownerClient = app.CreateClient();
        using var otherClient = app.CreateClient();
        var ownerEmail = UniqueEmail();
        var otherEmail = UniqueEmail();
        await CreateVerifiedAsync(app, ownerClient, ownerEmail);
        await CreateVerifiedAsync(app, otherClient, otherEmail);
        SetBearer(ownerClient, await LoginAsync(ownerClient, ownerEmail, Password));
        SetBearer(otherClient, await LoginAsync(otherClient, otherEmail, Password));
        var create = await ownerClient.PostAsJsonAsync("/api/workspaces/",
            new CreateWorkspaceRequest("revoked-" + Guid.NewGuid().ToString("N")[..12], "Revoked Test"));
        var workspace = (await create.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        Assert.Equal(HttpStatusCode.Accepted,
            (await ownerClient.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/invitations",
                new InviteWorkspaceRequest(otherEmail, WorkspaceRole.Viewer))).StatusCode);
        var rawToken = app.Mail.GetToken(otherEmail, "accept-invite");
        var invites = (await ownerClient.GetFromJsonAsync<List<InvitationSummary>>(
            $"/api/workspaces/{workspace.Id}/invitations"))!;
        var invite = Assert.Single(invites);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherClient.DeleteAsync($"/api/workspaces/{workspace.Id}/invitations/{invite.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.DeleteAsync($"/api/workspaces/{workspace.Id}/invitations/{invite.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await otherClient.PostAsJsonAsync("/api/invitations/accept",
                new AcceptInviteRequest(rawToken))).StatusCode);
        Assert.Empty((await otherClient.GetFromJsonAsync<List<WorkspaceSummary>>("/api/workspaces/"))!);
    }

    [Fact]
    public async Task SecurityAuditIsStoredWithoutSecrets_AndForgedJwtIsDenied()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        await CreateVerifiedAsync(app, client, email);
        var session = await LoginAsync(client, email, Password);
        var parts = session.AccessToken.Split('.');
        var signature = parts[2];
        var flip = signature[0] == 'A' ? 'B' : 'A';
        var forged = $"{parts[0]}.{parts[1]}.{flip}{signature[1..]}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/workspaces/")).StatusCode);
        SetBearer(client, session);
        var create = await client.PostAsJsonAsync("/api/workspaces/",
            new CreateWorkspaceRequest("audit-" + Guid.NewGuid().ToString("N")[..12], "Audit Workspace"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var workspace = (await create.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        await using var db = database.OpenContext();
        var events = await db.AuditEvents.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .Select(x => x.Action).ToListAsync();
        // Workspace creation is recorded as a tenancy security event.
        Assert.Contains("workspace.created", events);
        var credentialEvents = await db.AuditEvents.AsNoTracking()
            .Where(x => x.Action == "session.login")
            .ToListAsync();
        Assert.NotEmpty(credentialEvents);
        Assert.All(credentialEvents, x => Assert.True(x.Action.Length <= 64));
    }

    [Fact]
    public async Task WeakPasswordsAndMalformedAuthTokensFailClosed()
    {
        using var app = Start();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(UniqueEmail(), "weak"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("Not An Email", Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/refresh",
                new RefreshRequest("not-a-valid-refresh"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/verify-email",
                new VerifyEmailRequest("invalid"))).StatusCode);
    }


    [Fact]
    public async Task InvitedUserCanRegisterWhenPublicRegistrationIsDisabled()
    {
        using var ownerApp = Start(registration: true);
        using var ownerClient = ownerApp.CreateClient();
        var ownerEmail = UniqueEmail();
        var inviteeEmail = UniqueEmail();
        await CreateVerifiedAsync(ownerApp, ownerClient, ownerEmail);
        SetBearer(ownerClient, await LoginAsync(ownerClient, ownerEmail, Password));
        var created = await ownerClient.PostAsJsonAsync("/api/workspaces/",
            new CreateWorkspaceRequest("inviteonly-" + Guid.NewGuid().ToString("N")[..12], "Invite Only"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        Assert.Equal(HttpStatusCode.Accepted,
            (await ownerClient.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/invitations",
                new InviteWorkspaceRequest(inviteeEmail, WorkspaceRole.Viewer))).StatusCode);
        var token = ownerApp.Mail.GetToken(inviteeEmail, "accept-invite");

        using var closedApp = Start(registration: false);
        using var newcomer = closedApp.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await newcomer.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(inviteeEmail, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await newcomer.PostAsJsonAsync("/api/auth/register-with-invitation",
                new InviteRegisterRequest(token, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await newcomer.PostAsJsonAsync("/api/auth/register-with-invitation",
                new InviteRegisterRequest(token, Password))).StatusCode);
        SetBearer(newcomer, await LoginAsync(newcomer, inviteeEmail, Password));
        var list = (await newcomer.GetFromJsonAsync<List<WorkspaceSummary>>("/api/workspaces/"))!;
        Assert.Equal(workspace.Id, Assert.Single(list).Id);
        Assert.Equal(WorkspaceRole.Viewer, list[0].Role);
    }


    [Fact]
    public async Task KnownAccountLocksAfterFiveFailures_AndCanRecoverAfterExpiry()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        await CreateVerifiedAsync(app, client, email);
        for (var i = 0; i < 5; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(email, "Incorrect Password 99112"));
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(email, Password))).StatusCode);

        await using (var db = database.OpenContext())
        {
            var user = await db.Users.SingleAsync(x => x.EmailNormalized == email);
            Assert.True(user.LockoutUntilUtc > DateTimeOffset.UtcNow);
            Assert.Equal(0, user.FailedLoginCount);
            Assert.True(await db.AuditEvents.AnyAsync(x =>
                x.ActorUserId == user.Id && x.Action == "account.locked_by_failed_login"));
            // Simulate the end of the 15-minute lockout without sleeping.
            user.LockoutUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        var recovered = await LoginAsync(client, email, Password);
        Assert.False(string.IsNullOrWhiteSpace(recovered.AccessToken));
        await using var verified = database.OpenContext();
        var latest = await verified.Users.SingleAsync(x => x.EmailNormalized == email);
        Assert.Null(latest.LockoutUntilUtc);
        Assert.Equal(0, latest.FailedLoginCount);
    }


    [Fact]
    public async Task Phase2_ProjectCrud_IsTenantScopedAndRolesAreEnforced()
    {
        using var app = Start();
        using var ownerClient = app.CreateClient();
        using var memberClient = app.CreateClient();
        using var outsiderClient = app.CreateClient();
        var ownerEmail = UniqueEmail();
        var viewerEmail = UniqueEmail();
        var outsiderEmail = UniqueEmail();
        await CreateVerifiedAsync(app, ownerClient, ownerEmail);
        await CreateVerifiedAsync(app, memberClient, viewerEmail);
        await CreateVerifiedAsync(app, outsiderClient, outsiderEmail);
        SetBearer(ownerClient, await LoginAsync(ownerClient, ownerEmail, Password));
        SetBearer(memberClient, await LoginAsync(memberClient, viewerEmail, Password));
        SetBearer(outsiderClient, await LoginAsync(outsiderClient, outsiderEmail, Password));
        var response = await ownerClient.PostAsJsonAsync("/api/workspaces/",
            new CreateWorkspaceRequest("p2-" + Guid.NewGuid().ToString("N")[..15], "Phase 2 Workspace"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ws = (await response.Content.ReadFromJsonAsync<WorkspaceSummary>())!;

        var input = new ProjectInput("  Roadmap  ", "  Important tasks  ", ProjectStatus.Planned);
        var create = await ownerClient.PostAsJsonAsync($"/api/workspaces/{ws.Id}/projects", input);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var project = (await create.Content.ReadFromJsonAsync<ProjectView>())!;
        Assert.Equal("Roadmap", project.Name);
        Assert.Equal("Important tasks", project.Description);
        Assert.Equal(ws.Id, project.WorkspaceId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsiderClient.GetAsync($"/api/workspaces/{ws.Id}/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsiderClient.GetAsync($"/api/workspaces/{ws.Id}/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsiderClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/projects/{project.Id}",
                new ProjectInput("Stolen", "", ProjectStatus.Active))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsiderClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/settings",
                new WorkspaceSettingsInput("Hacked"))).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted,
            (await ownerClient.PostAsJsonAsync($"/api/workspaces/{ws.Id}/invitations",
                new InviteWorkspaceRequest(viewerEmail, WorkspaceRole.Viewer))).StatusCode);
        var raw = app.Mail.GetToken(viewerEmail, "accept-invite");
        Assert.Equal(HttpStatusCode.OK,
            (await memberClient.PostAsJsonAsync("/api/invitations/accept", new AcceptInviteRequest(raw))).StatusCode);
        var read = await memberClient.GetFromJsonAsync<List<ProjectView>>($"/api/workspaces/{ws.Id}/projects");
        Assert.Equal(project.Id, Assert.Single(read!).Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.PostAsJsonAsync($"/api/workspaces/{ws.Id}/projects", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.DeleteAsync($"/api/workspaces/{ws.Id}/projects/{project.Id}")).StatusCode);

        var changed = await ownerClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/projects/{project.Id}",
            new ProjectInput("Shipped", "Delivered", ProjectStatus.Completed));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var overview = (await ownerClient.GetFromJsonAsync<WorkspaceOverview>(
            $"/api/workspaces/{ws.Id}/overview"))!;
        Assert.Equal(2, overview.MemberCount);
        Assert.Equal(1, overview.ProjectCount);
        Assert.Equal(0, overview.ActiveProjects);
        Assert.Equal(1, overview.CompletedProjects);
        Assert.Contains(overview.RecentActivity, a => a.Action == "project.updated");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ownerClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/projects/{project.Id}",
                new ProjectInput("Hidden archive", "", ProjectStatus.Archived))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await ownerClient.DeleteAsync($"/api/workspaces/{ws.Id}/projects/{project.Id}")).StatusCode);
        Assert.Empty((await ownerClient.GetFromJsonAsync<List<ProjectView>>(
            $"/api/workspaces/{ws.Id}/projects"))!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ownerClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/projects/{project.Id}", input)).StatusCode);
    }

    [Fact]
    public async Task Phase2_SettingsRequireAdmin_AndDashboardDoesNotLeakInvitesToViewer()
    {
        using var app = Start();
        using var ownerClient = app.CreateClient();
        using var viewerClient = app.CreateClient();
        var ownerEmail = UniqueEmail();
        var viewerEmail = UniqueEmail();
        await CreateVerifiedAsync(app, ownerClient, ownerEmail);
        await CreateVerifiedAsync(app, viewerClient, viewerEmail);
        SetBearer(ownerClient, await LoginAsync(ownerClient, ownerEmail, Password));
        SetBearer(viewerClient, await LoginAsync(viewerClient, viewerEmail, Password));
        var created = await ownerClient.PostAsJsonAsync("/api/workspaces/",
            new CreateWorkspaceRequest("sett-" + Guid.NewGuid().ToString("N")[..15], "Original"));
        var ws = (await created.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        Assert.Equal(HttpStatusCode.Accepted,
            (await ownerClient.PostAsJsonAsync($"/api/workspaces/{ws.Id}/invitations",
                new InviteWorkspaceRequest(viewerEmail, WorkspaceRole.Viewer))).StatusCode);
        var raw = app.Mail.GetToken(viewerEmail, "accept-invite");
        Assert.Equal(HttpStatusCode.OK,
            (await viewerClient.PostAsJsonAsync("/api/invitations/accept", new AcceptInviteRequest(raw))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewerClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/settings",
                new WorkspaceSettingsInput("Forbidden"))).StatusCode);
        var settings = await ownerClient.PutAsJsonAsync($"/api/workspaces/{ws.Id}/settings",
            new WorkspaceSettingsInput("  Updated Space "));
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        var summary = (await settings.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        Assert.Equal("Updated Space", summary.DisplayName);
        Assert.Equal(ws.Slug, summary.Slug);
        var dashboard = (await viewerClient.GetFromJsonAsync<WorkspaceOverview>(
            $"/api/workspaces/{ws.Id}/overview"))!;
        Assert.Equal("Updated Space", dashboard.Name);
        Assert.Equal(0, dashboard.PendingInvitations);
        Assert.Contains(dashboard.RecentActivity, a => a.Action == "workspace.settings.updated");
    }

    [Fact]
    public async Task Phase2_InvalidProjectPayloadsAndMissingWorkspaceAreRejected()
    {
        using var app = Start();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        await CreateVerifiedAsync(app, client, email);
        SetBearer(client, await LoginAsync(client, email, Password));
        var created = await client.PostAsJsonAsync("/api/workspaces/",
            new CreateWorkspaceRequest("valid-" + Guid.NewGuid().ToString("N")[..15], "Valid"));
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceSummary>())!;
        foreach (var input in new[] {
            new ProjectInput(" ", "", ProjectStatus.Planned),
            new ProjectInput(new string('x',121), "", ProjectStatus.Planned),
            new ProjectInput("Valid",new string('x',2001), ProjectStatus.Planned),
            new ProjectInput("Valid","", (ProjectStatus)99),
            new ProjectInput("Archived","",ProjectStatus.Archived)
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/projects", input)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/projects")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<ProjectView>>(
            $"/api/workspaces/{workspace.Id}/projects"))!);
    }

    private const string Password = "Good Secure Pass 99112";
    private static string UniqueEmail() => $"{Guid.NewGuid():N}@example.invalid";

    private static async Task CreateVerifiedAsync(TestApp app, HttpClient client, string email)
    {
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password));
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        var token = app.Mail.GetToken(email, "verify-email");
        var response = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(token));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<TokenPair> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPair>())!;
    }

    private static void SetBearer(HttpClient client, TokenPair tokens) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

    private sealed class TestApp(string databaseConnection, bool registration = true) : WebApplicationFactory<Program>
    {
        public FakeSender Mail { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Auth:Enabled", "true");
            builder.UseSetting("Auth:RegistrationEnabled", registration ? "true" : "false");
            builder.UseSetting("Auth:RateLimitPerMinute", "120");
            builder.UseSetting("Auth:SigningKey", "test-only-not-for-production-a-long-strong-signing-key-12345");
            builder.UseSetting("Auth:Issuer", "ICEHOTT-AI-OS-Tests");
            builder.UseSetting("Auth:Audience", "ICEHOTT-Tests");
            builder.UseSetting("Auth:PublicBaseUrl", "http://localhost");
            builder.UseSetting("ConnectionStrings:Identity", databaseConnection);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth:Enabled"] = "true",
                    ["Auth:RegistrationEnabled"] = registration ? "true" : "false",
                    ["Auth:SigningKey"] = "test-only-not-for-production-a-long-strong-signing-key-12345",
                    ["Auth:Issuer"] = "ICEHOTT-AI-OS-Tests",
                    ["Auth:Audience"] = "ICEHOTT-Tests",
                    ["Auth:PublicBaseUrl"] = "http://localhost",
                    ["ConnectionStrings:Identity"] = databaseConnection
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountEmailSender>();
                services.AddSingleton<IAccountEmailSender>(Mail);
            });
        }
    }

    private sealed class FakeSender : IAccountEmailSender
    {
        private readonly ConcurrentQueue<(string Email, string Message)> _messages = new();

        public Task SendAsync(string recipient, string subject, string message, CancellationToken ct)
        {
            _messages.Enqueue((recipient, message));
            return Task.CompletedTask;
        }

        public string GetToken(string email, string purpose)
        {
            var entry = _messages.Last(x => x.Email == email && x.Message.Contains($"/{purpose}?token="));
            var match = Regex.Match(entry.Message, @"token=([A-Za-z0-9_-]+)");
            Assert.True(match.Success, "Expected token delivery in fake sender.");
            return match.Groups[1].Value;
        }
    }
}
