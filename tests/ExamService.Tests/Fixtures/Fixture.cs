using ExamService.Application.Common;
using ExamService.Infrastructure.Ai;
using ExamService.Application.Features;
using ExamService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;
using ExamService.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ExamService.Tests.Fixtures;

public sealed class DbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("examtest").WithUsername("exam").WithPassword("exam").Build();

    public string Connection => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options;
        await using var db = new AppDbContext(options, new StaticTenantContext());
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();
}

[CollectionDefinition("exam", DisableParallelization = true)]
public sealed class ExamCollection : ICollectionFixture<DbFixture>;

public sealed class TestTenant : ITenantContext
{
    public Guid TenantId { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? UserEmail { get; set; } = "examiner@test.local";
    public IReadOnlyList<string> Roles { get; set; } = ["Admin"];
}

public sealed class NoopStorage : IObjectStorage
{
    public Task EnsureBucketAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> PresignUploadAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult("https://storage.test/upload");
    public Task<string> PresignDownloadAsync(string key, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult("https://storage.test/download");
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(true);
}

[Collection("exam")]
public abstract class ExamTestBase(DbFixture fixture) : IAsyncLifetime
{
    protected ServiceProvider Services = null!;
    protected readonly TestTenant Tenant = new();
    protected MediatR.ISender Sender => Services.GetRequiredService<MediatR.ISender>();
    protected AppDbContext Db => Services.GetRequiredService<AppDbContext>();

    public Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Endpoint"] = "" }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ITenantContext>(Tenant);
        services.AddSingleton<IObjectStorage, NoopStorage>();
        services.AddSingleton<ExamService.Application.Common.IProctorNotifier, NoopNotifier>();
        services.AddSingleton<IAiEvaluator>(new AiEvaluator(config, new HttpClient()));
        services.AddSingleton<IEvidenceAnalyzer>(new EvidenceAnalyzer(config, new HttpClient()));
        services.AddSingleton(config);
        services.AddSingleton<HttpClient>();
        services.AddScoped(sp => new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Connection).Options, Tenant));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        Services = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await Services.DisposeAsync();
}

public sealed class NoopNotifier : ExamService.Application.Common.IProctorNotifier
{
    public Task PublishEventAsync(Guid tenantId, Guid examId, Guid attemptId, object payload, CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishFlagAsync(Guid tenantId, Guid examId, object payload, CancellationToken ct = default) => Task.CompletedTask;
}
