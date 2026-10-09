using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StaySphere.Application.Ai;
using StaySphere.Infrastructure.Ai;
using StaySphere.IntegrationTests.Infrastructure;

namespace StaySphere.IntegrationTests;

/// <summary>
/// Runs the real Agent Framework agent and the real toolbox (against SQL Server) through each provider's real client SDK,
/// with a local stub standing in for the model: the stub asks for a SearchProperties call, then answers with text.
/// Proves the provider wiring and the tool loop work without needing an API key or network access.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AiAgentProviderTests(ApiFactory factory)
{
    private const string FinalAnswer = "Stub model: I found a great flat for you.";

    [Fact]
    public async Task Gemini_agent_calls_tools_through_the_google_sdk()
    {
        await using var stub = await ModelStub.StartAsync(GeminiReply);
        var settings = AiProviderSettings.Resolve(
            new AiOptions { Provider = "Gemini", ApiKey = "test-key", BaseUrl = stub.Url }, _ => null);

        var (reply, toolsUsed, city) = await RunAgentAsync(settings);

        reply.ShouldBe(FinalAnswer);
        toolsUsed.ShouldContain(nameof(AssistantToolbox.SearchProperties));
        stub.Requests.Count.ShouldBe(2);
        stub.Requests.ShouldAllBe(r => r.Path.Contains($"models/{AiProviders.DefaultGeminiModel}:generateContent") && r.ApiKey == "test-key");
        stub.Requests[1].Body.ShouldContain(city); // the tool's real search result went back to the model
    }

    [Fact]
    public async Task Foundry_Local_agent_calls_tools_through_the_openai_compatible_client()
    {
        await using var stub = await ModelStub.StartAsync(OpenAiReply);
        var settings = AiProviderSettings.Resolve(
            new AiOptions { Provider = "FoundryLocal", Model = "phi-4-mini", BaseUrl = stub.Url + "/v1" }, _ => null);

        var (reply, toolsUsed, city) = await RunAgentAsync(settings);

        reply.ShouldBe(FinalAnswer);
        toolsUsed.ShouldContain(nameof(AssistantToolbox.SearchProperties));
        stub.Requests.Count.ShouldBe(2);
        stub.Requests.ShouldAllBe(r => r.Path == "/v1/chat/completions" && r.Authorization == "Bearer foundry-local");
        stub.Requests[1].Body.ShouldContain(city);
    }

    private async Task<(string Reply, IReadOnlyList<string> ToolsUsed, string City)> RunAgentAsync(AiProviderSettings settings)
    {
        settings.UsesAgent.ShouldBeTrue();
        var city = "Stubcity" + Guid.NewGuid().ToString("N")[..6];
        var (_, host) = await factory.RegisterHostAsync();
        await factory.CreatePublishedPropertyAsync(host.User.Id, city: city);

        using var chat = new ChatClientBuilder(AiChatClients.Create(settings, timeoutSeconds: 30)).UseFunctionInvocation().Build();
        var engine = new AgentFrameworkAssistantEngine(chat, settings, Options.Create(new AiOptions()), NullLoggerFactory.Instance);

        return await factory.WithScopeAsync(async sp =>
        {
            var toolbox = sp.GetRequiredService<AssistantToolbox>();
            var reply = await engine.RunAsync(new AssistantMemory(), $"Find me a place in {city}", toolbox, CancellationToken.None);
            return (reply, (IReadOnlyList<string>)toolbox.State.ToolsUsed.ToList(), city);
        });
    }

    // ---- Wire formats: first turn asks for a tool call, the turn after the tool result answers with text ----

    private static string GeminiReply(JsonNode request, string city)
    {
        var hasToolResult = request.ToJsonString().Contains("functionResponse", StringComparison.Ordinal);
        object part = hasToolResult
            ? new { text = FinalAnswer }
            : new { functionCall = new { name = "SearchProperties", args = new { location = city } } };
        return JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { role = "model", parts = new[] { part } }, finishReason = "STOP", index = 0 } },
            usageMetadata = new { promptTokenCount = 1, candidatesTokenCount = 1, totalTokenCount = 2 },
            modelVersion = "stub",
        });
    }

    private static string OpenAiReply(JsonNode request, string city)
    {
        var hasToolResult = request["messages"]!.AsArray().Any(m => (string?)m!["role"] == "tool");
        object message = hasToolResult
            ? new { role = "assistant", content = FinalAnswer }
            : new
            {
                role = "assistant",
                content = (string?)null,
                tool_calls = new[]
                {
                    new { id = "call_1", type = "function", function = new { name = "SearchProperties", arguments = JsonSerializer.Serialize(new { location = city }) } },
                },
            };
        return JsonSerializer.Serialize(new
        {
            id = "chatcmpl-stub",
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = (string?)request["model"],
            choices = new[] { new { index = 0, message, finish_reason = hasToolResult ? "stop" : "tool_calls" } },
            usage = new { prompt_tokens = 1, completion_tokens = 1, total_tokens = 2 },
        });
    }

    private sealed record StubRequest(string Path, string Body, string? ApiKey, string? Authorization);

    /// <summary>A tiny HTTP server on a random local port that plays the model.</summary>
    private sealed class ModelStub : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<StubRequest> _requests = new();

        private ModelStub(WebApplication app) => _app = app;

        public string Url { get; private set; } = "";
        public IReadOnlyList<StubRequest> Requests => _requests.ToList();

        public static async Task<ModelStub> StartAsync(Func<JsonNode, string, string> reply)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var stub = new ModelStub(app);
            app.MapPost("/{**path}", async (HttpContext ctx) =>
            {
                var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                stub._requests.Enqueue(new StubRequest(ctx.Request.Path + ctx.Request.QueryString, body,
                    ctx.Request.Headers["x-goog-api-key"].FirstOrDefault(), ctx.Request.Headers.Authorization.FirstOrDefault()));
                var city = System.Text.RegularExpressions.Regex.Match(body, "Stubcity[0-9a-f]{6}").Value;
                return Results.Content(reply(JsonNode.Parse(body)!, city), "application/json");
            });
            await app.StartAsync();
            stub.Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return stub;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
