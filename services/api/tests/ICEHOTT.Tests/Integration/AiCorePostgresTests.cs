using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ICEHOTT.Domain;
using ICEHOTT.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ICEHOTT.Tests.Integration;

[Collection("postgres-tenancy")]
public sealed class AiCorePostgresTests(TenantDatabaseFixture fixture)
{
    private const string Key = "phase3-only-test-secret-key-48bytes-DO-NOT-USE-PRODUCTION";
    private const string Issuer = "ICEHOTT-Phase3-Test";
    private const string Audience = "ICEHOTT-Phase3-Clients";

    [Fact]
    public async Task AuthenticatedWorkspaceChat_UsageReplayAndTenantIsolation()
    {
        using var app = new TestApp(fixture.ConnectionString);
        var (ownerClient, ownerId) = await ActorAsync(app);
        var (outsider, _) = await ActorAsync(app);
        using (ownerClient)
        using (outsider)
        {
            var workspace = await MakeWorkspaceAsync(ownerId);
            var other = await MakeWorkspaceAsync((await ActorAsync(app)).Id);
            var create = await ownerClient.PostAsJsonAsync(
                $"/api/workspaces/{workspace}/ai/conversations", new NewAiConversation("  Architecture  "));
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var chat = (await create.Content.ReadFromJsonAsync<AiConversationView>())!;
            Assert.Equal("Architecture", chat.Title);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await outsider.GetAsync($"/api/workspaces/{workspace}/ai/conversations")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await ownerClient.GetAsync($"/api/workspaces/{other}/ai/conversations/{chat.Id}/messages")).StatusCode);
            var unauthorized = await outsider.PostAsJsonAsync(
                $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                new AiSendRequest(Guid.NewGuid(), "Get secrets"));
            Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
            var id = Guid.NewGuid();
            var turn = await ownerClient.PostAsJsonAsync(
                $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                new AiSendRequest(id, "Help me plan a release"));
            Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
            var result = (await turn.Content.ReadFromJsonAsync<AiChatReply>())!;
            Assert.Equal(id, result.RequestId);
            Assert.Equal("demo-test", result.Provider);
            Assert.Contains("Demo", result.Answer);

            var repeat = await ownerClient.PostAsJsonAsync(
                $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                new AiSendRequest(id, "Help me plan a release"));
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
            Assert.Equal(result, await repeat.Content.ReadFromJsonAsync<AiChatReply>());
            Assert.Equal(1, app.Runtime.Calls);
            Assert.Equal(HttpStatusCode.Conflict,
                (await ownerClient.PostAsJsonAsync(
                    $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                    new AiSendRequest(id, "Different input"))).StatusCode);
            var messages = (await ownerClient.GetFromJsonAsync<List<AiMessageView>>(
                $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/messages"))!;
            Assert.Equal(2, messages.Count);
            Assert.Equal(AiMessageRole.User, messages[0].Role);
            Assert.Equal(AiMessageRole.Assistant, messages[1].Role);

            var second = await ownerClient.PostAsJsonAsync(
                $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                new AiSendRequest(Guid.NewGuid(), "Second message"));
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var exceeded = await ownerClient.PostAsJsonAsync(
                $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                new AiSendRequest(Guid.NewGuid(), "Third request"));
            Assert.Equal(HttpStatusCode.TooManyRequests, exceeded.StatusCode);
            var usage = (await ownerClient.GetFromJsonAsync<AiUsageView>(
                $"/api/workspaces/{workspace}/ai/usage"))!;
            Assert.Equal(2, usage.RequestsToday);
            Assert.Equal(2, usage.DailyRequestLimit);
            Assert.True(usage.ReservedTokensToday > 0);
            Assert.Contains((await ownerClient.GetFromJsonAsync<List<AiConversationView>>(
                $"/api/workspaces/{workspace}/ai/conversations"))!, x => x.Id == chat.Id);
        }
    }

    [Fact]
    public async Task ViewerCanReadButCannotCreateOrSend_UnknownConversationIs404()
    {
        using var app = new TestApp(fixture.ConnectionString);
        var (owner, ownerId) = await ActorAsync(app);
        var (viewer, viewerId) = await ActorAsync(app);
        using (owner)
        using (viewer)
        {
            var ws = await MakeWorkspaceAsync(ownerId);
            await using (var db = fixture.OpenContext())
            {
                db.Memberships.Add(new WorkspaceMembership
                {
                    WorkspaceId = ws, UserId = viewerId, Role = WorkspaceRole.Viewer,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }
            var created = await owner.PostAsJsonAsync(
                $"/api/workspaces/{ws}/ai/conversations", new NewAiConversation("Tenant Safe"));
            var chat = (await created.Content.ReadFromJsonAsync<AiConversationView>())!;
            Assert.Equal(HttpStatusCode.OK,
                (await viewer.GetAsync($"/api/workspaces/{ws}/ai/conversations")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await viewer.PostAsJsonAsync($"/api/workspaces/{ws}/ai/conversations",
                    new NewAiConversation("Forbidden"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await viewer.PostAsJsonAsync($"/api/workspaces/{ws}/ai/conversations/{chat.Id}/send",
                    new AiSendRequest(Guid.NewGuid(), "not allowed"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await owner.GetAsync(
                    $"/api/workspaces/{ws}/ai/conversations/{Guid.NewGuid()}/messages")).StatusCode);
        }
    }

    [Fact]
    public async Task FailedProviderDoesNotSaveConversationOrDoubleCallOnRetry()
    {
        using var app = new TestApp(fixture.ConnectionString);
        app.Runtime.Fail = true;
        var (client, id) = await ActorAsync(app);
        using (client)
        {
            var ws = await MakeWorkspaceAsync(id);
            var create = await client.PostAsJsonAsync(
                $"/api/workspaces/{ws}/ai/conversations", new NewAiConversation("Fail closed"));
            var chat = (await create.Content.ReadFromJsonAsync<AiConversationView>())!;
            var req = new AiSendRequest(Guid.NewGuid(), "Please answer");
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.PostAsJsonAsync(
                    $"/api/workspaces/{ws}/ai/conversations/{chat.Id}/send", req)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync(
                    $"/api/workspaces/{ws}/ai/conversations/{chat.Id}/send", req)).StatusCode);
            Assert.Equal(1, app.Runtime.Calls);
            Assert.Empty((await client.GetFromJsonAsync<List<AiMessageView>>(
                $"/api/workspaces/{ws}/ai/conversations/{chat.Id}/messages"))!);
            var usage = (await client.GetFromJsonAsync<AiUsageView>(
                $"/api/workspaces/{ws}/ai/usage"))!;
            Assert.Equal(1, usage.RequestsToday);
            await using var db = fixture.OpenContext();
            Assert.True(await db.AiAttempts.AnyAsync(x => x.WorkspaceId == ws &&
                x.Status == AiAttemptStatus.Failed));
        }
    }

    [Fact]
    public async Task InputValidationAndAnonymousAccessFailClosed()
    {
        using var app = new TestApp(fixture.ConnectionString);
        using var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync($"/api/workspaces/{Guid.NewGuid()}/ai/conversations")).StatusCode);
        var (client, id) = await ActorAsync(app);
        using (client)
        {
            var ws = await MakeWorkspaceAsync(id);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync($"/api/workspaces/{ws}/ai/conversations",
                    new NewAiConversation(new string('x', 101)))).StatusCode);
            var created = await client.PostAsJsonAsync(
                $"/api/workspaces/{ws}/ai/conversations", new NewAiConversation("Validate"));
            var chat = (await created.Content.ReadFromJsonAsync<AiConversationView>())!;
            foreach (var text in new[] { " ", new string('x', 4001), "hello\0world" })
                Assert.Equal(HttpStatusCode.BadRequest,
                    (await client.PostAsJsonAsync($"/api/workspaces/{ws}/ai/conversations/{chat.Id}/send",
                        new AiSendRequest(Guid.NewGuid(), text))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync($"/api/workspaces/{ws}/ai/conversations/{chat.Id}/send",
                    new AiSendRequest(Guid.Empty, "Hello"))).StatusCode);
            Assert.Equal(0, app.Runtime.Calls);
        }
    }

    [Fact]
    public async Task PerMinuteRateLimitIsDatabaseEnforcedBeforeInvokingProvider()
    {
        using var app = new TestApp(fixture.ConnectionString, dailyLimit: 30, minuteLimit: 3);
        var (client, actor) = await ActorAsync(app);
        using (client)
        {
            var workspace = await MakeWorkspaceAsync(actor);
            var create = await client.PostAsJsonAsync($"/api/workspaces/{workspace}/ai/conversations",
                new NewAiConversation("Minute window"));
            var chat = (await create.Content.ReadFromJsonAsync<AiConversationView>())!;
            for (var i = 0; i < 3; i++)
            {
                var result = await client.PostAsJsonAsync(
                    $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                    new AiSendRequest(Guid.NewGuid(), "Test the rate limit"));
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            }
            Assert.Equal(HttpStatusCode.TooManyRequests,
                (await client.PostAsJsonAsync(
                    $"/api/workspaces/{workspace}/ai/conversations/{chat.Id}/send",
                    new AiSendRequest(Guid.NewGuid(), "Fourth in one minute"))).StatusCode);
            Assert.Equal(3, app.Runtime.Calls);
        }
    }

    private async Task<Guid> MakeWorkspaceAsync(Guid id)
    {
        await using var db = fixture.OpenContext();
        var access = new WorkspaceAccessService(db);
        return (await access.CreateAsync(id, "ai-" + Guid.NewGuid().ToString("N")[..16], "AI Workspace")).Id;
    }

    private async Task<(HttpClient Client, Guid Id)> ActorAsync(TestApp app)
    {
        var user = await fixture.CreateUserAsync();
        var family = Guid.NewGuid();
        await using (var db = fixture.OpenContext())
        {
            var record = await db.Users.SingleAsync(x => x.Id == user.Id);
            record.EmailVerifiedAtUtc = DateTimeOffset.UtcNow;
            db.RefreshSessions.Add(new RefreshSession
            {
                Id = Guid.NewGuid(), UserId = user.Id, FamilyId = family,
                TokenHash = AuthCrypto.HashToken(AuthCrypto.NewToken()),
                CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }
        var jwt = new JwtSecurityToken(Issuer, Audience, new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new Claim("sid", family.ToString("D"))
        }, notBefore: DateTime.UtcNow.AddSeconds(-1), expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new JwtSecurityTokenHandler().WriteToken(jwt));
        return (client, user.Id);
    }

    private sealed class TestApp(string connectionString, int dailyLimit = 2, int minuteLimit = 5) : WebApplicationFactory<Program>
    {
        public FakeRuntime Runtime { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Auth:Enabled", "true");
            builder.UseSetting("Auth:RegistrationEnabled", "false");
            builder.UseSetting("Auth:SigningKey", Key);
            builder.UseSetting("Auth:Issuer", Issuer);
            builder.UseSetting("Auth:Audience", Audience);
            builder.UseSetting("Auth:PublicBaseUrl", "http://localhost");
            builder.UseSetting("ConnectionStrings:Identity", connectionString);
            builder.UseSetting("Ai:Enabled", "true");
            builder.UseSetting("Ai:RuntimeUrl", "http://127.0.0.1:8000");
            builder.UseSetting("Ai:SharedSecret", "test-only-unique-32-byte-secret-NOT-LIVE");
            builder.UseSetting("Ai:DailyRequestLimit", dailyLimit.ToString());
            builder.UseSetting("Ai:PerMinuteRequestLimit", minuteLimit.ToString());
            builder.UseSetting("Ai:DailyTokenLimit", "30000");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiRuntime>();
                services.AddSingleton<IAiRuntime>(Runtime);
            });
        }
    }

    private sealed class FakeRuntime : IAiRuntime
    {
        public int Calls;
        public bool Fail;
        public Task<AiRuntimeResponse> GenerateAsync(AiRuntimeRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (Fail) throw new HttpRequestException("simulated upstream secret that must never be returned");
            return Task.FromResult(new AiRuntimeResponse("Demo response", "demo-test", 100));
        }
    }
}
