using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StaySphere.Api.Common;
using StaySphere.Api.Hubs;
using StaySphere.Application;
using StaySphere.Application.Abstractions;
using StaySphere.Infrastructure;
using StaySphere.Infrastructure.Identity;
using StaySphere.Infrastructure.Persistence;
using StaySphere.Infrastructure.Seeding;
using StaySphere.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Logging & telemetry ----------
builder.Host.UseSerilog((ctx, services, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "StaySphere.Api")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}"));

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("staysphere-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
        .AddHttpClientInstrumentation()
        .AddSource("Azure.*", "StaySphere"))
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentationIfAvailable()
        .AddMeter("StaySphere", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http"));
if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    otel.UseOtlpExporter();

// ---------- Core services ----------
builder.Services.AddApplication(config);
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddFeatureManagement();

builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = false);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddMvc().AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'V";
    o.SubstituteApiVersionInUrl = true;
});

// ---------- AuthN / AuthZ ----------
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Set it via user-secrets, environment variables or Key Vault.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = ClaimTypes.Role,
        NameClaimType = "name",
    };
    o.Events = new JwtBearerEvents
    {
        // Browsers cannot set headers on WebSockets: accept the token from the query string for the hub only.
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddAuthorization(Policies.Register);

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { type = "https://staysphere.dev/errors/rate-limited", title = "Too many requests. Please slow down.", status = 429 }, ct);
    };
    static string Partition(HttpContext c) => c.User.FindFirst("sub")?.Value ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var scale = config.GetValue("RateLimiting:Multiplier", 1);
    void Window(string name, int permits, TimeSpan window) => o.AddPolicy(name, c =>
        RateLimitPartition.GetFixedWindowLimiter(Partition(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = permits * scale, Window = window, QueueLimit = 0 }));
    Window(RateLimitPolicies.Anonymous, 120, TimeSpan.FromMinutes(1));
    Window(RateLimitPolicies.Auth, 30, TimeSpan.FromMinutes(1));
    Window(RateLimitPolicies.AuthStrict, 10, TimeSpan.FromMinutes(1));
    Window(RateLimitPolicies.Payment, 10, TimeSpan.FromMinutes(1));
    Window(RateLimitPolicies.Ai, 20, TimeSpan.FromMinutes(1));
    Window(RateLimitPolicies.Messaging, 40, TimeSpan.FromMinutes(1));
    Window(RateLimitPolicies.Upload, 30, TimeSpan.FromMinutes(1));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Partition(c), _ => new FixedWindowRateLimiterOptions { PermitLimit = 600 * scale, Window = TimeSpan.FromMinutes(1) }));
});

// ---------- Real-time ----------
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(config.GetConnectionString("Redis")))
    signalR.AddStackExchangeRedis(config.GetConnectionString("Redis")!, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("staysphere"));
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<IPresenceTracker>(sp => sp.GetRequiredService<PresenceTracker>());
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

// ---------- CORS ----------
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    .WithExposedHeaders("Idempotent-Replayed", "Retry-After")));

// ---------- OpenAPI ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StaySphere API",
        Version = "v1",
        Description = "Accommodation marketplace API. Errors use RFC 9457 ProblemDetails. Mutating payment/booking endpoints require an Idempotency-Key header.",
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the accessToken from /api/v1/auth/login.",
    });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    var xml = Path.Combine(AppContext.BaseDirectory, "StaySphere.Api.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
    o.SupportNonNullableReferenceTypes();
    o.NonNullableReferenceTypesAsRequired();
    o.OperationFilter<SuccessResponseOperationFilter>();
    o.CustomSchemaIds(SchemaId);
    static string SchemaId(Type t) => t.IsGenericType
        ? t.Name[..t.Name.IndexOf('`', StringComparison.Ordinal)] + "Of" + string.Join("And", t.GetGenericArguments().Select(a => SchemaId(a).Split('.').Last()))
        : t.FullName!.Replace("StaySphere.Contracts.", string.Empty, StringComparison.Ordinal).Replace("StaySphere.Application.Abstractions.", string.Empty, StringComparison.Ordinal).Replace('+', '.');
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ---------- Pipeline ----------
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) app.UseHsts();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ctx.RequestAborted.IsCancellationRequested ? Serilog.Events.LogEventLevel.Debug
    : ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);

if (config.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger(o => o.RouteTemplate = "openapi/{documentName}.json");
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "StaySphere API v1");
        o.RoutePrefix = "swagger";
    });
}

var storage = config.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
if (storage.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
{
    var mediaRoot = Path.GetFullPath(storage.LocalPath);
    Directory.CreateDirectory(mediaRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(mediaRoot),
        RequestPath = storage.LocalPublicBaseUrl,
        OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
    });
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/realtime");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = HealthWriter.WriteAsync });
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthWriter.WriteAsync });
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

await InitializeDatabaseAsync(app);
await app.RunAsync();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var config = app.Configuration;
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    // Production migrations run as a gated deployment step (migration bundle), never implicitly on app start.
    if (config.GetValue("Database:MigrateOnStartup", false) && !app.Environment.IsProduction())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                break;
            }
            catch (Exception ex) when (attempt < 20)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    if (config.GetValue("Database:Seed", false) && !app.Environment.IsProduction())
        await scope.ServiceProvider.GetRequiredService<DevDataSeeder>().SeedAsync(CancellationToken.None);
}

internal static class HealthWriter
{
    public static Task WriteAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), e.Value.Description, durationMs = e.Value.Duration.TotalMilliseconds }),
        });
    }
}

internal static class OtelExtensions
{
    public static MeterProviderBuilder AddRuntimeInstrumentationIfAvailable(this MeterProviderBuilder builder) => builder.AddMeter("System.Runtime");
}

public partial class Program;
