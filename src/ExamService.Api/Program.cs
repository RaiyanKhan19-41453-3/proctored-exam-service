using CoachingPlatform.Shared;
using ExamService.Api;
using ExamService.Application;
using ExamService.Application.Common;
using ExamService.Infrastructure.Ai;
using ExamService.Infrastructure.Persistence;
using ExamService.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Default")
    ?? "Host=db;Database=examservice;Username=exam;Password=exam";

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));
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

builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("db", tags: ["ready"]);
builder.Services.AddSingleton(new DbHealthCheck(connection));

builder.Services.AddSignalR();
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
app.UseMiddleware<ErrorMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors("web");
app.MapControllers();
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
recurring.AddOrUpdate<ReportWorker>("generate-reports", w => w.GeneratePendingAsync(), "*/1 * * * *");
app.Run();
