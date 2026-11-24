using ExamService.Application.Common;
using MediatR;
using ExamService.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using ExamService.Domain.Entities;

namespace ExamService.Application.Features.Reports;

public sealed record PublishResults(Guid ExamId) : IRequest<int>, IRequiresTenant;

public sealed class PublishResultsHandler(IAppDbContext db) : IRequestHandler<PublishResults, int>
{
    public async Task<int> Handle(PublishResults request, CancellationToken ct)
    {
        var reports = await db.Set<EvaluationReport>().Where(r => r.ExamId == request.ExamId).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var report in reports) report.PublishedAt ??= now;
        if (reports.Count > 0) await db.SaveChangesAsync(ct);
        return reports.Count(r => r.PublishedAt == now);
    }
}

public sealed record GetMyResult(Guid AttemptId) : IRequest<ResultResponse?>, IRequiresTenant;

public sealed class GetMyResultHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<GetMyResult, ResultResponse?>
{
    public async Task<ResultResponse?> Handle(GetMyResult request, CancellationToken ct)
    {
        await db.OwnedAsync(current, request.AttemptId, ct);
        var report = await db.Set<EvaluationReport>().FirstOrDefaultAsync(r => r.AttemptId == request.AttemptId, ct);
        if (report is null || report.PublishedAt is null) return null;
        return new ResultResponse(report.Id, report.TotalScore, report.IntegrityScore,
            report.Decision, true, report.AiSummary);
    }
}
