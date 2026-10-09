using StaySphere.Application.Ai;

namespace StaySphere.UnitTests.Application;

public sealed class AiProviderSettingsTests
{
    private static AiProviderSettings Resolve(AiOptions options, Dictionary<string, string>? env = null) =>
        AiProviderSettings.Resolve(options, name => env?.GetValueOrDefault(name));

    [Fact]
    public void Gemini_is_the_default_provider()
    {
        new AiOptions().Provider.ShouldBe(AiProviders.Gemini);
    }

    [Fact]
    public void Gemini_without_a_key_falls_back_to_rules_and_explains_how_to_enable_it()
    {
        var settings = Resolve(new AiOptions());

        settings.UsesAgent.ShouldBeFalse();
        settings.DisabledReason!.ShouldContain("GEMINI_API_KEY");
    }

    [Theory]
    [InlineData("GEMINI_API_KEY")]
    [InlineData("GOOGLE_API_KEY")]
    public void Gemini_reads_the_key_from_the_environment_and_uses_the_default_model(string variable)
    {
        var settings = Resolve(new AiOptions(), new() { [variable] = "abc" });

        settings.UsesAgent.ShouldBeTrue();
        settings.ApiKey.ShouldBe("abc");
        settings.Model.ShouldBe(AiProviders.DefaultGeminiModel);
    }

    [Fact]
    public void Configured_key_and_model_win_over_defaults()
    {
        var settings = Resolve(new AiOptions { Provider = "gemini", ApiKey = "configured", Model = "gemini-pinned" },
            new() { ["GEMINI_API_KEY"] = "from-env" });

        settings.Provider.ShouldBe(AiProviders.Gemini);
        settings.ApiKey.ShouldBe("configured");
        settings.Model.ShouldBe("gemini-pinned");
    }

    [Fact]
    public void Foundry_Local_needs_its_endpoint_and_model_but_no_key()
    {
        Resolve(new AiOptions { Provider = "FoundryLocal", Model = "phi-4-mini" }).DisabledReason!.ShouldContain("foundry service status");
        Resolve(new AiOptions { Provider = "FoundryLocal", BaseUrl = "http://localhost:5273/v1" }).DisabledReason!.ShouldContain("foundry model list");

        var settings = Resolve(new AiOptions { Provider = "FoundryLocal", BaseUrl = "http://localhost:5273/v1", Model = "phi-4-mini" });
        settings.UsesAgent.ShouldBeTrue();
        settings.BaseUrl.ShouldBe(new Uri("http://localhost:5273/v1"));
        settings.ApiKey.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Ollama_defaults_to_localhost_and_OpenAI_needs_a_key()
    {
        Resolve(new AiOptions { Provider = "Ollama", Model = "llama3.2" }).BaseUrl.ShouldBe(new Uri("http://localhost:11434"));
        Resolve(new AiOptions { Provider = "OpenAI", Model = "gpt-x" }).UsesAgent.ShouldBeFalse();
        Resolve(new AiOptions { Provider = "OpenAI", Model = "gpt-x" }, new() { ["OPENAI_API_KEY"] = "k" }).UsesAgent.ShouldBeTrue();
    }

    [Fact]
    public void Rules_never_uses_an_agent()
    {
        var settings = Resolve(new AiOptions { Provider = "Rules" }, new() { ["GEMINI_API_KEY"] = "abc" });

        settings.UsesAgent.ShouldBeFalse();
        settings.DisabledReason.ShouldBeNull();
    }

    [Theory]
    [InlineData("Claude-ish")]
    [InlineData("")]
    public void Unknown_provider_fails_fast(string provider)
    {
        Should.Throw<InvalidOperationException>(() => Resolve(new AiOptions { Provider = provider }));
    }

    [Fact]
    public void Relative_base_url_fails_fast()
    {
        Should.Throw<InvalidOperationException>(() => Resolve(new AiOptions { Provider = "Gemini", BaseUrl = "localhost:1234" }));
    }
}
