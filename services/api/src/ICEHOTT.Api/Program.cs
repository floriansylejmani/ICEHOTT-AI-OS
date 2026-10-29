using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ICEHOTT.Domain;
using ICEHOTT.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));

var auth = builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new();
if (auth.Enabled)
{
    if (Encoding.UTF8.GetByteCount(auth.SigningKey) < 32 ||
        string.IsNullOrWhiteSpace(auth.Issuer) || string.IsNullOrWhiteSpace(auth.Audience) ||
        string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Identity")))
        throw new InvalidOperationException("Auth requires server-side JWT key (>=32 bytes), issuer, audience, and Identity DB.");
    if (!Uri.TryCreate(auth.PublicBaseUrl, UriKind.Absolute, out var publicUrl) ||
        (!builder.Environment.IsDevelopment() && publicUrl.Scheme != "https"))
        throw new InvalidOperationException("Auth requires a valid HTTPS public URL outside development.");
    if (!builder.Environment.IsDevelopment() &&
        new[] { "Email:Smtp:Host", "Email:Smtp:Username", "Email:Smtp:Password", "Email:Smtp:From" }
            .Any(key => string.IsNullOrWhiteSpace(builder.Configuration[key])))
        throw new InvalidOperationException("Enabled authentication requires configured SMTP in hosted environments.");

    builder.Services.AddDbContext<IdentityDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Identity")));
    builder.Services.AddScoped<AccountService>();
    builder.Services.AddScoped<WorkspaceAccessService>();
    builder.Services.AddScoped<WorkspaceMembershipService>();
    builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
    builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
    builder.Services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidIssuer = auth.Issuer,
                ValidAudience = auth.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
                ClockSkew = TimeSpan.FromSeconds(15),
                NameClaimType = JwtRegisteredClaimNames.Sub
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var principal = ctx.Principal;
                    if (!Guid.TryParse(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ||
                        !Guid.TryParse(principal?.FindFirstValue("sid"), out var sessionId) ||
                        !await ctx.HttpContext.RequestServices.GetRequiredService<AccountService>()
                            .IsActiveSessionAsync(userId, sessionId, ctx.HttpContext.RequestAborted))
                        ctx.Fail("Session revoked or invalid.");
                }
            };
        });
    builder.Services.AddAuthorization();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("auth", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Clamp(builder.Configuration.GetValue<int?>("Auth:RateLimitPerMinute") ?? 10, 1, 120),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    });
}

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async ctx =>
{
    var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    ctx.Response.StatusCode = exception switch
    {
        AuthRejectedException => StatusCodes.Status401Unauthorized,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        AuthConflictException or WorkspaceSlugAlreadyTakenException => StatusCodes.Status409Conflict,
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = ctx.Response.StatusCode,
        Title = ctx.Response.StatusCode == 500 ? "Internal server error" : "Request rejected"
    });
}));
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/auth"))
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers.Pragma = "no-cache";
    }
    await next();
});
app.MapHealthChecks("/health/live");
app.MapGet("/api/system", () => Results.Ok(new
{
    product = "ICEHOTT AI OS", version = "0.2.0", phase = "Phase 1",
    authEnabled = auth.Enabled
}));

if (auth.Enabled)
{
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    TokenPair Issue(SessionTokens session)
    {
        var expiration = DateTimeOffset.UtcNow.AddMinutes(10);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, session.UserId.ToString("D")),
            new Claim("sid", session.FamilyId.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var jwt = new JwtSecurityToken(
            issuer: auth.Issuer,
            audience: auth.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiration.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new TokenPair(new JwtSecurityTokenHandler().WriteToken(jwt),
            session.RefreshToken, expiration);
    }

    var authRoutes = app.MapGroup("/api/auth").RequireRateLimiting("auth");
    authRoutes.MapPost("/register", async (RegisterRequest request, AccountService service, CancellationToken ct) =>
    {
        if (!auth.RegistrationEnabled) return Results.NotFound();
        await service.RegisterAsync(request, ct);
        return Results.Accepted();
    });
    authRoutes.MapPost("/register-with-invitation", async (
        InviteRegisterRequest request, WorkspaceMembershipService service, CancellationToken ct) =>
    {
        await service.RegisterWithInvitationAsync(request, ct);
        return Results.NoContent();
    });
    authRoutes.MapPost("/verify-email", async (VerifyEmailRequest request, AccountService service, CancellationToken ct) =>
    {
        await service.VerifyEmailAsync(request.Token, ct);
        return Results.NoContent();
    });
    authRoutes.MapPost("/resend-verification", async (PasswordResetRequest request, AccountService service, CancellationToken ct) =>
    {
        await service.SendVerificationAsync(request.Email, ct);
        return Results.Accepted();
    });
    authRoutes.MapPost("/request-password-reset", async (PasswordResetRequest request, AccountService service, CancellationToken ct) =>
    {
        await service.RequestResetAsync(request.Email, ct);
        return Results.Accepted();
    });
    authRoutes.MapPost("/reset-password", async (PasswordResetConfirm request, AccountService service, CancellationToken ct) =>
    {
        await service.ConfirmResetAsync(request, ct);
        return Results.NoContent();
    });
    authRoutes.MapPost("/login", async (LoginRequest request, AccountService service, CancellationToken ct) =>
        Results.Ok(Issue(await service.LoginAsync(request, ct))));
    authRoutes.MapPost("/refresh", async (RefreshRequest request, AccountService service, CancellationToken ct) =>
        Results.Ok(Issue(await service.RotateAsync(request.RefreshToken, ct))));
    authRoutes.MapPost("/logout", async (ClaimsPrincipal actor, AccountService service, CancellationToken ct) =>
    {
        await service.RevokeFamilyAsync(CurrentUser(actor), CurrentSession(actor), ct);
        return Results.NoContent();
    }).RequireAuthorization();

    var workspaces = app.MapGroup("/api/workspaces").RequireAuthorization();
    workspaces.MapGet("/", (ClaimsPrincipal actor, WorkspaceAccessService service, CancellationToken ct) =>
        service.ListMineAsync(CurrentUser(actor), ct));
    workspaces.MapPost("/", async (ClaimsPrincipal actor, CreateWorkspaceRequest request,
        WorkspaceAccessService service, CancellationToken ct) =>
    {
        var record = await service.CreateAsync(CurrentUser(actor), request.Slug, request.Name, ct);
        return Results.Created($"/api/workspaces/{record.Id}", record);
    });
    workspaces.MapGet("/{workspaceId:guid}/members", (ClaimsPrincipal actor, Guid workspaceId,
        WorkspaceMembershipService service, CancellationToken ct) =>
        service.ListMembersAsync(CurrentUser(actor), workspaceId, ct));
    workspaces.MapGet("/{workspaceId:guid}/invitations", (ClaimsPrincipal actor, Guid workspaceId,
        WorkspaceMembershipService service, CancellationToken ct) =>
        service.ListPendingInvitationsAsync(CurrentUser(actor), workspaceId, ct));
    workspaces.MapPost("/{workspaceId:guid}/invitations", async (ClaimsPrincipal actor, Guid workspaceId,
        InviteWorkspaceRequest request, WorkspaceMembershipService service, CancellationToken ct) =>
    {
        await service.InviteAsync(CurrentUser(actor), workspaceId, request, ct);
        return Results.Accepted();
    });
    workspaces.MapDelete("/{workspaceId:guid}/invitations/{invitationId:guid}", async (
        ClaimsPrincipal actor, Guid workspaceId, Guid invitationId, WorkspaceMembershipService service, CancellationToken ct) =>
    {
        await service.RevokeInvitationAsync(CurrentUser(actor), workspaceId, invitationId, ct);
        return Results.NoContent();
    });
    app.MapPost("/api/invitations/accept", async (
        ClaimsPrincipal actor, AcceptInviteRequest request, WorkspaceMembershipService service, CancellationToken ct) =>
        Results.Ok(await service.AcceptAsync(CurrentUser(actor), request.Token, ct))).RequireAuthorization();
    workspaces.MapPut("/{workspaceId:guid}/members/{userId:guid}/role", async (
        ClaimsPrincipal actor, Guid workspaceId, Guid userId, ChangeRoleRequest request,
        WorkspaceMembershipService service, CancellationToken ct) =>
    {
        await service.ChangeRoleAsync(CurrentUser(actor), workspaceId, userId, request.Role, ct);
        return Results.NoContent();
    });
    workspaces.MapDelete("/{workspaceId:guid}/members/{userId:guid}", async (
        ClaimsPrincipal actor, Guid workspaceId, Guid userId, WorkspaceMembershipService service, CancellationToken ct) =>
    {
        await service.RemoveAsync(CurrentUser(actor), workspaceId, userId, ct);
        return Results.NoContent();
    });
    workspaces.MapPost("/{workspaceId:guid}/transfer-ownership/{userId:guid}", async (
        ClaimsPrincipal actor, Guid workspaceId, Guid userId, WorkspaceMembershipService service, CancellationToken ct) =>
    {
        await service.TransferOwnershipAsync(CurrentUser(actor), workspaceId, userId, ct);
        return Results.NoContent();
    });
}

app.Run();

static Guid CurrentUser(ClaimsPrincipal principal) =>
    Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
        ? id : throw new AuthRejectedException();

static Guid CurrentSession(ClaimsPrincipal principal) =>
    Guid.TryParse(principal.FindFirstValue("sid"), out var id)
        ? id : throw new AuthRejectedException();

public partial class Program { }
