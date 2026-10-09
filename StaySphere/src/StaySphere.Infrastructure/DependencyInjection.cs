using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Ai;
using StaySphere.Infrastructure.Ai;
using StaySphere.Infrastructure.Caching;
using StaySphere.Infrastructure.Email;
using StaySphere.Infrastructure.External;
using StaySphere.Infrastructure.Identity;
using StaySphere.Infrastructure.Jobs;
using StaySphere.Infrastructure.Messaging;
using StaySphere.Infrastructure.Payments;
using StaySphere.Infrastructure.Persistence;
using StaySphere.Infrastructure.Seeding;
using StaySphere.Infrastructure.Storage;

namespace StaySphere.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Wires every port to an adapter. Each Azure dependency has a local/dev alternative chosen by configuration.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<QuoteOptions>(configuration.GetSection(QuoteOptions.Section));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.Configure<FakePaymentOptions>(configuration.GetSection(FakePaymentOptions.Section));
        services.Configure<ExternalServicesOptions>(configuration.GetSection(ExternalServicesOptions.Section));
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.Section));
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.Section));

        // Persistence
        var connectionString = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("ConnectionStrings:Sql is not configured.");
        services.AddSingleton<OutboxAndAuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(3);
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "platform");
            })
            .AddInterceptors(sp.GetRequiredService<OutboxAndAuditInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DevDataSeeder>();

        // Security
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IQuoteTokenService, HmacQuoteTokenService>();

        // Cache: Redis when configured, otherwise in-process (single instance dev).
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "staysphere:"; });
        else
            services.AddDistributedMemoryCache();
        services.AddSingleton<ICacheService, DistributedCacheService>();

        // Storage + images
        var storage = configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
        if (storage.Provider.Equals("Blob", StringComparison.OrdinalIgnoreCase))
        {
            // Azure: Managed Identity via AccountUrl (no keys). Local: Azurite connection string.
            services.AddSingleton(_ => string.IsNullOrWhiteSpace(storage.ConnectionString)
                ? new BlobServiceClient(new Uri(storage.AccountUrl ?? throw new InvalidOperationException("Storage:AccountUrl or Storage:ConnectionString is required.")), new DefaultAzureCredential())
                : new BlobServiceClient(storage.ConnectionString));
            services.AddSingleton<IFileStorageService, BlobFileStorageService>();
        }
        else
        {
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }

        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<IMalwareScanner, BasicMalwareScanner>();

        // Email
        var email = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
        if (email.Provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Payments — adapter chosen here; Stripe/Adyen adapters would register under the same port.
        services.AddHttpClient("fake-payments-webhook", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IPaymentProvider, LocalFakePaymentProvider>();
        services.AddSingleton<IPayoutProvider, FakePayoutProvider>();

        AddExternalServices(services, configuration);
        AddMessaging(services, configuration);
        AddAi(services, configuration);

        services.AddHealthChecks()
            .AddSqlServer(connectionString, name: "sql", tags: ["ready"])
            .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"]);
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddHealthChecks().AddRedis(redis, name: "redis", tags: ["ready"]);
        if (storage.Provider.Equals("Blob", StringComparison.OrdinalIgnoreCase))
            services.AddHealthChecks().AddAzureBlobStorage(name: "blob", tags: ["ready"]);

        return services;
    }

    private static void AddExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        var external = configuration.GetSection(ExternalServicesOptions.Section).Get<ExternalServicesOptions>() ?? new ExternalServicesOptions();
        services.AddSingleton<ITaxRateProvider, ConfigTaxRateProvider>();
        services.AddSingleton<MockCurrencyService>();

        if (!external.Mode.Equals("Live", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IGeocodingService, MockGeocodingService>();
            services.AddSingleton<IWeatherService, MockWeatherService>();
            services.AddSingleton<ICurrencyService>(sp => sp.GetRequiredService<MockCurrencyService>());
            return;
        }

        // Every outbound call: timeout + retry (idempotent GETs only) + circuit breaker via the standard resilience pipeline.
        services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(c =>
        {
            c.BaseAddress = new Uri(external.NominatimBaseUrl);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(external.UserAgent);
        }).AddStandardResilienceHandler();
        services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(c => c.BaseAddress = new Uri(external.OpenMeteoBaseUrl))
            .AddStandardResilienceHandler();
        services.AddHttpClient<ICurrencyService, FrankfurterCurrencyService>(c => c.BaseAddress = new Uri(external.FrankfurterBaseUrl))
            .AddStandardResilienceHandler();
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var messaging = configuration.GetSection(MessagingOptions.Section).Get<MessagingOptions>() ?? new MessagingOptions();
        var workers = configuration.GetSection(WorkerOptions.Section).Get<WorkerOptions>() ?? new WorkerOptions();

        if (messaging.Transport.Equals("ServiceBus", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(_ => string.IsNullOrWhiteSpace(messaging.ServiceBusConnectionString)
                ? new ServiceBusClient(messaging.ServiceBusNamespace ?? throw new InvalidOperationException("Messaging:ServiceBusNamespace or connection string is required."), new DefaultAzureCredential())
                : new ServiceBusClient(messaging.ServiceBusConnectionString));
            services.AddSingleton<IMessageBus, ServiceBusMessageBus>();
            if (workers.Consumers) services.AddHostedService<ServiceBusConsumer>();
        }
        else
        {
            services.AddSingleton<InMemoryMessageBus>();
            services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<InMemoryMessageBus>());
            if (workers.Consumers) services.AddHostedService<InMemoryConsumer>();
        }

        services.AddSingleton<OutboxPublisher>();
        if (workers.Outbox) services.AddHostedService(sp => sp.GetRequiredService<OutboxPublisher>());
        if (workers.Jobs) services.AddHostedService<ScheduledJobs>();
    }

    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();
        var ai = AiProviderSettings.Resolve(options, name => configuration[name]);
        services.AddSingleton(ai);
        services.AddHostedService<AiStartupReport>();
        if (!ai.UsesAgent) return;

        services.TryAddSingleton<IChatClient>(sp => new ChatClientBuilder(AiChatClients.Create(ai, options.TimeoutSeconds))
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 8)
            .Build(sp));
        services.AddScoped<IAssistantEngine, AgentFrameworkAssistantEngine>();
    }
}

/// <summary>Readiness signal: a growing backlog of failed/unpublished outbox messages means messaging is unhealthy.</summary>
public sealed class OutboxHealthCheck(IServiceScopeFactory scopes) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        var stuck = await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && (m.FailedAt != null || m.OccurredAt < cutoff), cancellationToken);
        return stuck switch
        {
            0 => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            < 100 => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded($"{stuck} outbox message(s) delayed or failed."),
            _ => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"{stuck} outbox message(s) delayed or failed."),
        };
    }
}
