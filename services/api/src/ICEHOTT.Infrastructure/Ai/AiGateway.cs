using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ICEHOTT.Infrastructure.Ai;

public sealed class AiOptions
{
    public bool Enabled { get; set; }
    public string InternalUrl { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public int MaxRequestsPerDay { get; set; } = 30;
    public int MaxOutputTokens { get; set; } = 256;
}

public sealed record AiContextItem(string Role, string Content);
public sealed record AiGatewayResult(string Answer, string Provider,
    [property: JsonPropertyName("used_tokens")] int UsedTokens);
public interface IAiGateway
{
    Task<AiGatewayResult> GenerateAsync(string prompt, IReadOnlyList<AiContextItem> context,
        Guid traceId, int maxOutputTokens, CancellationToken ct);
}

public sealed class AiDisabledException : Exception;
public sealed class AiQuotaException : Exception;
public sealed class AiGatewayUnavailableException : Exception;

public sealed class SignedAiGateway(HttpClient client, IOptions<AiOptions> settings) : IAiGateway
{
    public async Task<AiGatewayResult> GenerateAsync(string prompt, IReadOnlyList<AiContextItem> context,
        Guid traceId, int maxOutputTokens, CancellationToken ct)
    {
        var config = settings.Value;
        if (!config.Enabled || Encoding.UTF8.GetByteCount(config.SharedSecret) < 32 ||
            !Uri.TryCreate(config.InternalUrl, UriKind.Absolute, out var baseUri))
            throw new AiDisabledException();

        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt,
            context = context.Select(x => new { role = x.Role, content = x.Content }),
            trace_id = traceId,
            max_output_tokens = maxOutputTokens
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var digest = Convert.ToHexStringLower(SHA256.HashData(body));
        var signed = Encoding.ASCII.GetBytes(timestamp + "." + digest);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.SharedSecret), signed);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/internal/generate"))
        {
            Content = new ByteArrayContent(body)
        };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        message.Headers.TryAddWithoutValidation("x-icehott-timestamp", timestamp);
        message.Headers.TryAddWithoutValidation("x-icehott-signature", Convert.ToHexStringLower(mac));
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new AiGatewayUnavailableException();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var output = await JsonSerializer.DeserializeAsync<AiGatewayResult>(
                stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            if (output is null || string.IsNullOrWhiteSpace(output.Answer) ||
                output.Answer.Length > 4096 || string.IsNullOrWhiteSpace(output.Provider) ||
                output.Provider.Length > 32 || output.UsedTokens is < 0 or > 10000)
                throw new AiGatewayUnavailableException();
            return output;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiGatewayUnavailableException();
        }
        catch (HttpRequestException)
        {
            throw new AiGatewayUnavailableException();
        }
        catch (JsonException)
        {
            throw new AiGatewayUnavailableException();
        }
    }
}
