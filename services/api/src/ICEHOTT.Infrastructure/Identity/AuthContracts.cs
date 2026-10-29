using ICEHOTT.Domain;

namespace ICEHOTT.Infrastructure.Identity;

public sealed class AuthSettings
{
    public bool Enabled { get; set; }
    public bool RegistrationEnabled { get; set; }
    public string Issuer { get; set; } = "ICEHOTT-AI-OS";
    public string Audience { get; set; } = "ICEHOTT-AI-OS-Web";
    public string SigningKey { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
}
public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);
public sealed record PasswordResetRequest(string Email);
public sealed record PasswordResetConfirm(string Token, string NewPassword);
public sealed record VerifyEmailRequest(string Token);
public sealed record CreateWorkspaceRequest(string Slug, string Name);
public sealed record InviteWorkspaceRequest(string Email, WorkspaceRole Role);
public sealed record AcceptInviteRequest(string Token);
public sealed record InviteRegisterRequest(string Token, string Password);
public sealed record ChangeRoleRequest(WorkspaceRole Role);

public interface IAccountEmailSender
{
    Task SendAsync(string recipient, string subject, string message, CancellationToken cancellationToken);
}
