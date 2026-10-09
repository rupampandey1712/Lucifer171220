using System.ClientModel;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;
using StaySphere.Application.Ai;

namespace StaySphere.Infrastructure.Ai;

/// <summary>
/// Creates the provider-specific <see cref="IChatClient"/> under the Agent Framework agent. Everything above this line
/// (agent, tools, confirmation gate) is identical for every provider.
/// </summary>
public static class AiChatClients
{
    public static IChatClient Create(AiProviderSettings ai, int timeoutSeconds) => ai.Provider switch
    {
        // Official Google Gen AI SDK; it implements Microsoft.Extensions.AI natively (function calling included).
        AiProviders.Gemini => new Client(
                apiKey: ai.ApiKey,
                httpOptions: new HttpOptions { BaseUrl = ai.BaseUrl?.ToString(), Timeout = timeoutSeconds * 1000 })
            .AsIChatClient(ai.Model),

        // Foundry Local and any other OpenAI-compatible server (OpenAI, Azure OpenAI v1, LM Studio, vLLM…).
        AiProviders.FoundryLocal or AiProviders.OpenAI => new OpenAIClient(
                new ApiKeyCredential(ai.ApiKey!),
                new OpenAIClientOptions { Endpoint = ai.BaseUrl, NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds) })
            .GetChatClient(ai.Model)
            .AsIChatClient(),

        AiProviders.Ollama => new OllamaApiClient(ai.BaseUrl!, ai.Model!),

        _ => throw new InvalidOperationException($"No chat client for AI provider '{ai.Provider}'."),
    };
}

/// <summary>Logs once at startup which assistant is active, and how to enable the agent when it isn't.</summary>
public sealed class AiStartupReport(AiProviderSettings ai, ILogger<AiStartupReport> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (ai.UsesAgent)
            logger.LogInformation("AI assistant: Agent Framework agent on {Provider} (model {Model})", ai.Provider, ai.Model);
        else if (ai.DisabledReason is not null)
            logger.LogWarning("AI assistant: {Provider} is not configured, so the offline rule-based assistant answers instead. {Reason}",
                ai.Provider, ai.DisabledReason);
        else
            logger.LogInformation("AI assistant: offline rule-based engine (AI:Provider=Rules)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
