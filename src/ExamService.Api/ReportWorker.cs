using ExamService.Application.Common;
using ExamService.Application.Features.Reports;
using Hangfire;
using ExamService.Domain.Common;
using ExamService.Application.Features.Sessions;
using ExamService.Application.Features.Anomalies;
using ExamService.Application.Features.Evidence;
using ExamService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Api;

/// <summary>
/// Generates reports automatically for submitted attempts so examiners only review exceptions.
/// Hangfire storage is this service's own Postgres database.
/// </summary>
public sealed class ReportWorker(IServiceScopeFactory scopes, ILogger<ReportWorker> logger)
{
    public async Task GeneratePendingAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContextShim>().Context;
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
        var pending = await db.Attempts
            .Where(a => a.Status == AttemptStatus.UnderReview &&
                        !db.Reports.Any(r => r.AttemptId == a.Id))
            .Select(a => a.Id).Take(100).ToListAsync();

        foreach (var id in pending)
        {
            try { await sender.Send(new GenerateReport(id)); }
            catch (Exception ex) { logger.LogError(ex, "Report failed for attempt {Id}", id); }
        }
    }
}

public sealed class AppDbContextShim(Infrastructure.Persistence.AppDbContext context)
{
    public Infrastructure.Persistence.AppDbContext Context => context;
}

public sealed class DeadlineWorker(IServiceScopeFactory scopes, ILogger<DeadlineWorker> logger)
{
    public async Task ExpireAsync()
    {
        using var scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
        var count = await sender.Send(new ExpireAttempts());
        if (count > 0) logger.LogInformation("Auto-submitted {Count} expired attempts", count);
    }
}

public sealed class AnomalyWorker(IServiceScopeFactory scopes, ILogger<AnomalyWorker> logger)
{
    public async Task DetectAllAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContextShim>().Context;
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
        var exams = await db.Exams.Where(e => e.Status == ExamStatus.Published)
            .Select(e => e.Id).ToListAsync();
        foreach (var exam in exams)
        {
            try { await sender.Send(new DetectAnomalies(exam)); }
            catch (Exception ex) { logger.LogError(ex, "Anomaly detection failed for exam {Id}", exam); }
        }
    }
}

public sealed class RetentionWorker(IServiceScopeFactory scopes, ILogger<RetentionWorker> logger)
{
    public async Task CleanupAsync()
    {
        using var scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
        var removed = await sender.Send(new CleanupExpiredEvidence());
        if (removed > 0) logger.LogInformation("Removed {Count} expired evidence records", removed);
    }
}

public sealed class ResultNotificationWorker(IServiceScopeFactory scopes, ILogger<ResultNotificationWorker> logger)
{
    public async Task DispatchAsync()
    {
        using var scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
        var sent = await sender.Send(new DispatchResultNotifications());
        if (sent > 0) logger.LogInformation("Dispatched {Count} result notifications", sent);
    }
}
