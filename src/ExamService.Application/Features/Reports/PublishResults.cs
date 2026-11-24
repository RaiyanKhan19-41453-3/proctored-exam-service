using ExamService.Application.Common;
using MediatR;
using ExamService.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ExamService.Domain.Entities;
using ExamService.Domain.Common;

namespace ExamService.Application.Features.Reports;

public sealed record PublishResults(Guid ExamId, bool IncludeFlagged = false) : IRequest<int>, IRequiresTenant;

public sealed class PublishResultsHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<PublishResults, int>
{
    public async Task<int> Handle(PublishResults request, CancellationToken ct)
    {
        var reports = await db.Set<EvaluationReport>().Where(r => r.ExamId == request.ExamId).ToListAsync(ct);
        if (!request.IncludeFlagged)
            reports = reports.Where(r => r.Decision == ReviewDecision.Approved ||
                                         r.Decision == ReviewDecision.Rejected).ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var report in reports) report.PublishedAt ??= now;
        var published = reports.Count(r => r.PublishedAt == now);
        if (published > 0)
        {
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                TenantId = current.TenantId, EventType = EventTypes.ResultsPublished,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    ExamId = request.ExamId, PublishedCount = published,
                    CandidateIds = reports.Where(r => r.PublishedAt == now)
                        .Select(r => r.CandidateId).Distinct().ToList()
                }),
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
        }
        return published;
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
