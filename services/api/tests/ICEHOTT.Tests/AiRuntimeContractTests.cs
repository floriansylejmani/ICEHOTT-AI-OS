using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICEHOTT.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace ICEHOTT.Tests;

public sealed class AiRuntimeContractTests
{
    [Fact]
    public async Task DotNetToFastApi_WireContractHasSignedBodyAndAccurateUsage()
    {
        const string shared = "Phase3-contract-32-byte-secret-test-only-not-live";
        var transport = new InspectionHandler(shared);
        using var http = new HttpClient(transport);
        var runtime = new HttpAiRuntime(http, Options.Create(new AiCoreSettings
        {
            Enabled = true,
            RuntimeUrl = "http://127.0.0.1:8000",
            SharedSecret = shared,
            DailyRequestLimit = 30,
            DailyTokenLimit = 30000
        }));
        var result = await runtime.GenerateAsync(new AiRuntimeRequest("release plan",
            new List<string> { "safe context" }, 256, Guid.NewGuid()), CancellationToken.None);
        Assert.Equal("Demo answer", result.Answer);
        Assert.Equal("demo-offline", result.Provider);
        Assert.Equal(42, result.UsedTokens);
        Assert.True(transport.SignatureVerified);
    }

    private sealed class InspectionHandler(string sharedSecret) : HttpMessageHandler
    {
        public bool SignatureVerified;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("http://127.0.0.1:8000/internal/generate", request.RequestUri?.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("release plan", json.RootElement.GetProperty("prompt").GetString());
            Assert.Equal(256, json.RootElement.GetProperty("max_output_tokens").GetInt32());
            Assert.True(Guid.TryParse(json.RootElement.GetProperty("trace_id").GetString(), out _));
            var timestamp = request.Headers.GetValues("x-icehott-timestamp").Single();
            var sent = request.Headers.GetValues("x-icehott-signature").Single();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            var expected = Convert.ToHexString(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(sharedSecret), Encoding.UTF8.GetBytes(timestamp + "." + hash))).ToLowerInvariant();
            SignatureVerified = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(expected));
            Assert.True(SignatureVerified);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"answer\":\"Demo answer\",\"provider\":\"demo-offline\",\"used_tokens\":42}",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
