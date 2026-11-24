using ExamService.Application.Common;
using ExamService.Application.Features.Reports;
using Hangfire;
using ExamService.Domain.Common;
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
