using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ICEHOTT.Infrastructure.Identity;

public sealed class AiCoreSettings
{
    public bool Enabled { get; set; }
    public string RuntimeUrl { get; set; } = "";
    public string SharedSecret { get; set; } = "";
    public int DailyRequestLimit { get; set; } = 30;
    public int PerMinuteRequestLimit { get; set; } = 5;
    public int DailyTokenLimit { get; set; } = 30000;
}

public sealed record AiRuntimeRequest(string Prompt, IReadOnlyList<string> Context, int MaxOutputTokens, Guid TraceId);
public sealed record AiRuntimeResponse(string Answer, string Provider, [property: System.Text.Json.Serialization.JsonPropertyName("used_tokens")] int UsedTokens);

public interface IAiRuntime
{
    Task<AiRuntimeResponse> GenerateAsync(AiRuntimeRequest request, CancellationToken ct);
}

public sealed class HttpAiRuntime(HttpClient http, IOptions<AiCoreSettings> settings) : IAiRuntime
{
    public async Task<AiRuntimeResponse> GenerateAsync(AiRuntimeRequest request, CancellationToken ct)
    {
        var policy = settings.Value;
        if (!policy.Enabled || string.IsNullOrEmpty(policy.RuntimeUrl) ||
            Encoding.UTF8.GetByteCount(policy.SharedSecret) < 32)
            throw new InvalidOperationException("AI runtime not explicitly enabled.");
        var body = JsonSerializer.Serialize(new
        {
            prompt = request.Prompt,
            context = request.Context,
            max_output_tokens = request.MaxOutputTokens,
            trace_id = request.TraceId.ToString("D")
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(policy.SharedSecret),
            Encoding.UTF8.GetBytes(timestamp + "." + hash))).ToLowerInvariant();
        using var message = new HttpRequestMessage(HttpMethod.Post, policy.RuntimeUrl.TrimEnd('/') + "/internal/generate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        message.Headers.TryAddWithoutValidation("x-icehott-timestamp", timestamp);
        message.Headers.TryAddWithoutValidation("x-icehott-signature", signature);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var wire = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var result = new AiRuntimeResponse(
            wire.GetProperty("answer").GetString() ?? "",
            wire.GetProperty("provider").GetString() ?? "",
            wire.GetProperty("used_tokens").GetInt32());
        if (string.IsNullOrWhiteSpace(result.Answer) || result.Answer.Length > 4096 ||
            result.UsedTokens <= 0 || result.UsedTokens > 2000 || string.IsNullOrWhiteSpace(result.Provider))
            throw new InvalidOperationException("AI runtime returned invalid response.");
        return result;
    }
}
