using System.Threading.RateLimiting;
using CoachingPlatform.Shared;
using ExamService.Api;
using ExamService.Application;
using ExamService.Application.Features.Identity;
using ExamService.Application.Common;
using ExamService.Infrastructure.Ai;
using ExamService.Application.Features.Sessions;
using ExamService.Application.Features.Anomalies;
using ExamService.Application.Features.Evidence;
using ExamService.Application.Features.Reports;
using ExamService.Infrastructure.Persistence;
using ExamService.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using ExamService.Api.RateLimiting;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, services, config) => config
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());
var connection = builder.Configuration.GetConnectionString("Default")
    ?? "Host=db;Database=examservice;Username=exam;Password=exam";

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetNoLimiter("infrastructure");
        var tenant = ctx.User?.FindFirst("tenant_id")?.Value ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(tenant, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = int.TryParse(builder.Configuration["RateLimit:PerMinute"], out var limit) ? limit : 600,
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        });
    });
});
var telemetry = builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        t.AddSource("Npgsql");
    })
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(otlp))
{
    telemetry.WithTracing(t => t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp)));
    telemetry.WithMetrics(m => m.AddOtlpExporter(o => o.Endpoint = new Uri(otlp)));
}

builder.Services.AddApplication();
builder.Services.AddControllers();
builder.Services.AddProductJwt(builder.Configuration);
builder.Services.AddScoped<AppDbContextShim>();

builder.Services.AddSingleton<IObjectStorage>(new MinioStorage(
    builder.Configuration["Minio:Endpoint"] ?? "storage:9000",
    builder.Configuration["Minio:AccessKey"] ?? "exam",
    builder.Configuration["Minio:SecretKey"] ?? "exam",
    builder.Configuration["Minio:Bucket"] ?? "exam-recordings",
    builder.Configuration["Minio:PublicEndpoint"] ?? "http://localhost:9010"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAiEvaluator, AiEvaluator>();
builder.Services.AddSingleton<IEvidenceAnalyzer, EvidenceAnalyzer>();

builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("db", tags: ["ready"]);
builder.Services.AddSingleton(new DbHealthCheck(connection));

var signalR = builder.Services.AddSignalR();
var signalRRedis = builder.Configuration["Redis:ConnectionString"];
IConnectionMultiplexer? sharedRedis = null;
if (!string.IsNullOrWhiteSpace(signalRRedis))
{
    var options = ConfigurationOptions.Parse(signalRRedis!);
    options.AbortOnConnectFail = false;
    sharedRedis = ConnectionMultiplexer.Connect(options);
    builder.Services.AddSingleton(sharedRedis);
}
if (!string.IsNullOrWhiteSpace(signalRRedis))
    signalR.AddStackExchangeRedis(signalRRedis!,
        o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("exam-proctor:"));
builder.Services.AddSingleton<IProctorNotifier, ProctorNotifier>();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:13010")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddHangfire(c => c
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connection)));
builder.Services.AddHangfireServer();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseMiddleware<ErrorMiddleware>();
app.UseAuthentication();
app.UseMiddleware<RedisRateLimitMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.UseCors("web");
app.MapControllers();
app.MapHub<ProctorHub>("/hubs/proctor").RequireAuthorization();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    for (var i = 1; i <= 15; i++)
    {
        try { await db.Database.MigrateAsync(); break; }
        catch (Exception) when (i < 15) { await Task.Delay(2000); }
    }
    try { await app.Services.GetRequiredService<IObjectStorage>().EnsureBucketAsync(); }
    catch (Exception ex) { app.Logger.LogWarning(ex, "Object storage not ready"); }
}

var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
recurring.AddOrUpdate<ResultNotificationWorker>("result-notifications", w => w.DispatchAsync(), "*/1 * * * *");
recurring.AddOrUpdate<RetentionWorker>("evidence-retention", w => w.CleanupAsync(), Cron.Daily);
recurring.AddOrUpdate<AnomalyWorker>("detect-anomalies", w => w.DetectAllAsync(), "*/5 * * * *");
recurring.AddOrUpdate<DeadlineWorker>("expire-attempts", w => w.ExpireAsync(), "*/1 * * * * *");
recurring.AddOrUpdate<ReportWorker>("generate-reports", w => w.GeneratePendingAsync(), "*/1 * * * *");
app.Run();
