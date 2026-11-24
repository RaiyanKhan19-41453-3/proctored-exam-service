using ExamService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ExamService.Api;

public sealed class DbHealthCheck(string connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
            await using var db = new AppDbContext(options, new StaticTenantContext());
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("Postgres reachable")
                : HealthCheckResult.Unhealthy("Postgres unreachable");
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy(ex.Message); }
    }
}
