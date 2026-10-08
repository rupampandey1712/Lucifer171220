using System.Data;
using System.Threading.Channels;
using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaySphere.Application.Abstractions;
using StaySphere.Application.Events;
using StaySphere.Infrastructure.Persistence;

namespace StaySphere.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string Section = "Messaging";
    /// <summary>InMemory (single process) or ServiceBus (Azure Service Bus / the official emulator).</summary>
    public string Transport { get; set; } = "InMemory";
    public string? ServiceBusConnectionString { get; set; }
    public string TopicName { get; set; } = "staysphere.events";
    public string SubscriptionName { get; set; } = "staysphere-workers";
    public int OutboxBatchSize { get; set; } = 50;
    public int OutboxPollMilliseconds { get; set; } = 1000;
}

public sealed class WorkerOptions
{
    public const string Section = "Workers";
    public bool Outbox { get; set; } = true;
    public bool Consumers { get; set; } = true;
    public bool Jobs { get; set; } = true;
}

// ---------- In-memory transport (local dev, tests) ----------

public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly Channel<OutboundMessage> _channel = Channel.CreateBounded<OutboundMessage>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
    });

    public ChannelReader<OutboundMessage> Reader => _channel.Reader;

    public Task PublishAsync(OutboundMessage message, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(message, cancellationToken).AsTask();
}

public sealed class InMemoryConsumer(InMemoryMessageBus bus, IntegrationEventDispatcher dispatcher, ILogger<InMemoryConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in bus.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await dispatcher.DispatchAsync(message.MessageId, message.Type, message.Payload, stoppingToken);
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    if (attempt >= 5)
                    {
                        // In-memory "dead letter": logged with full context for replay. Service Bus has a real DLQ.
                        logger.LogError(ex, "Dead-lettered {Type} {MessageId} after {Attempts} attempts", message.Type, message.MessageId, attempt);
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt * attempt), stoppingToken);
                }
            }
        }
    }
}

// ---------- Azure Service Bus transport ----------

public sealed class ServiceBusMessageBus(ServiceBusClient client, IOptions<MessagingOptions> options) : IMessageBus, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(options.Value.TopicName);

    public Task PublishAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var sbMessage = new ServiceBusMessage(message.Payload)
        {
            MessageId = message.MessageId.ToString(), // duplicate detection key
            Subject = message.Type,
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
        };
        sbMessage.ApplicationProperties["EventType"] = message.Type;
        return _sender.SendMessageAsync(sbMessage, cancellationToken);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

public sealed class ServiceBusConsumer(ServiceBusClient client, IntegrationEventDispatcher dispatcher, IOptions<MessagingOptions> options,
    ILogger<ServiceBusConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(options.Value.TopicName, options.Value.SubscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 8,
        });
        processor.ProcessMessageAsync += async args =>
        {
            var type = args.Message.ApplicationProperties.TryGetValue("EventType", out var t) ? t?.ToString() ?? args.Message.Subject : args.Message.Subject;
            await dispatcher.DispatchAsync(Guid.Parse(args.Message.MessageId), type, args.Message.Body.ToString(), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };
        processor.ProcessErrorAsync += args =>
        {
            // Unhandled → message is abandoned and redelivered; after MaxDeliveryCount Service Bus dead-letters it.
            logger.LogError(args.Exception, "Service Bus processing error ({Source})", args.ErrorSource);
            return Task.CompletedTask;
        };
        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }

        await processor.StopProcessingAsync(CancellationToken.None);
    }
}

// ---------- Outbox publisher ----------

/// <summary>
/// Publishes committed outbox rows. Rows are claimed with UPDLOCK/READPAST so any number of replicas can run this
/// concurrently without double-publishing; Service Bus duplicate detection (MessageId) and consumer inboxes cover the
/// remaining at-least-once window. Failures back off exponentially and stop after <c>OutboxMessage.MaxAttempts</c>.
/// </summary>
public sealed class OutboxPublisher(IServiceScopeFactory scopes, IMessageBus bus, IOptions<MessagingOptions> options, TimeProvider clock,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private sealed record Claimed(Guid Id, string Type, string Payload, string? CorrelationId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox publishing cycle failed");
            }

            if (published < options.Value.OutboxBatchSize)
                await Task.Delay(options.Value.OutboxPollMilliseconds, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    public async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var claimed = await ClaimAsync(db, ct);
        foreach (var message in claimed)
        {
            var now = clock.GetUtcNow();
            try
            {
                await bus.PublishAsync(new OutboundMessage(message.Id, message.Type, message.Payload, message.CorrelationId), ct);
                await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, now), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Publishing outbox message {MessageId} ({Type}) failed", message.Id, message.Type);
                var row = await db.OutboxMessages.FirstAsync(m => m.Id == message.Id, ct);
                row.MarkFailedAttempt(ex.Message, now);
                await db.SaveChangesAsync(ct);
            }
        }

        return claimed.Count;
    }

    private async Task<List<Claimed>> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        const string sql = """
            WITH batch AS (
                SELECT TOP (@batch) * FROM [platform].[OutboxMessages] WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE [ProcessedAt] IS NULL AND [FailedAt] IS NULL
                  AND ([NextAttemptAt] IS NULL OR [NextAttemptAt] <= @now)
                  AND ([LockedUntil] IS NULL OR [LockedUntil] < @now)
                ORDER BY [OccurredAt])
            UPDATE batch SET [LockedUntil] = @lockUntil
            OUTPUT inserted.[Id], inserted.[Type], inserted.[Payload], inserted.[CorrelationId];
            """;
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var now = clock.GetUtcNow();
        command.Parameters.Add(new SqlParameter("@batch", options.Value.OutboxBatchSize));
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@lockUntil", now.AddSeconds(60)));
        var result = new List<Claimed>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new Claimed(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return result;
    }
}
