using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ICEHOTT.Infrastructure.Identity;

/// <summary>
/// SMTP with TLS in hosted environments. A no-auth plaintext exception is
/// permitted only for an explicitly opted-in loopback development mail catcher.
/// Never logs raw tokens, credentials or email bodies.
/// </summary>
public sealed class SmtpAccountEmailSender(IConfiguration config, IHostEnvironment environment) : IAccountEmailSender
{
    public async Task SendAsync(string recipient, string subject, string message, CancellationToken cancellationToken)
    {
        var host = config["Email:Smtp:Host"];
        var from = config["Email:Smtp:From"];
        var user = config["Email:Smtp:Username"];
        var secret = config["Email:Smtp:Password"];
        var localDev = environment.IsDevelopment() &&
            (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(host, "127.0.0.1", StringComparison.Ordinal)) &&
            string.Equals(config["Email:Smtp:AllowPlaintextLocal"], "true",
                StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            (!localDev && (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(secret))))
            throw new InvalidOperationException("Secure SMTP delivery is not configured.");

        var port = int.TryParse(config["Email:Smtp:Port"], out var configuredPort)
            ? configuredPort : localDev ? 1025 : 587;
        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = !localDev,
            UseDefaultCredentials = false,
            Credentials = localDev ? null : new NetworkCredential(user, secret)
        };
        using var mail = new MailMessage(from, recipient, subject, message);
        await smtp.SendMailAsync(mail, cancellationToken);
    }
}
