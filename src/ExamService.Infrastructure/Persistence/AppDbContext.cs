using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options)
        => _tenant = tenant;

    public Guid CurrentTenantId => _tenant.TenantId;

    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<ExamAttempt> Attempts => Set<ExamAttempt>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<ProctorEvent> ProctorEvents => Set<ProctorEvent>();
    public DbSet<Recording> Recordings => Set<Recording>();
    public DbSet<EvaluationReport> Reports => Set<EvaluationReport>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entity.ClrType)) continue;
            var p = Expression.Parameter(entity.ClrType, "e");
            var eq = Expression.Equal(
                Expression.Property(p, nameof(ITenantEntity.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
            b.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(eq, p));
        }
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
