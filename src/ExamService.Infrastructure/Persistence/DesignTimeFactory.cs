using ExamService.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ExamService.Infrastructure.Persistence;

public sealed class StaticTenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
}

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Database=examservice;Username=exam;Password=exam";
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options,
            new StaticTenantContext());
    }
}
