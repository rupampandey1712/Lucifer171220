using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StaySphere.Application;
using StaySphere.Application.Abstractions;
using StaySphere.Infrastructure;
using StaySphere.Workers;

// Background worker host: outbox publisher, Service Bus consumers (notifications, refunds, analytics, fraud, search
// cache) and scheduled jobs. Scales independently of the API (KEDA on queue depth in Azure Container Apps).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "StaySphere.Workers")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("staysphere-workers"))
    .WithTracing(t => t.AddHttpClientInstrumentation().AddSource("Azure.*", "StaySphere"));
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    otel.UseOtlpExporter();

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// No HTTP context in workers: handlers run as the system, and real-time pushes are skipped (clients re-sync;
// with the Redis backplane the API instances deliver live updates).
builder.Services.AddSingleton<ICurrentUser, SystemUser>();
builder.Services.AddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();
builder.Services.AddSingleton<IPresenceTracker, NullRealtimeNotifier>();

var host = builder.Build();
await host.RunAsync();
