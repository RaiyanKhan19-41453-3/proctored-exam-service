using ExamService.Application.Common;
using ExamService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Sessions;

/// <summary>Chronological proctor events for every attempt of an exam.</summary>
public sealed record ProctorHistory(Guid ExamId) : IRequest<IReadOnlyList<ProctorHistoryItem>>;

public sealed record ProctorHistoryItem(Guid AttemptId, string CandidateName, string Type,
    string Severity, DateTimeOffset At, string? Detail);

public sealed class ProctorHistoryHandler(IAppDbContext db)
    : IRequestHandler<ProctorHistory, IReadOnlyList<ProctorHistoryItem>>
{
    public async Task<IReadOnlyList<ProctorHistoryItem>> Handle(ProctorHistory request, CancellationToken ct)
    {
        IQueryable<ProctorHistoryItem> query =
            from e in db.Set<ProctorEvent>()
            join a in db.Set<ExamAttempt>() on e.AttemptId equals a.Id
            where a.ExamId == request.ExamId
            orderby e.OccurredAt descending
            select new ProctorHistoryItem(a.Id, a.CandidateName, e.EventType.ToString(),
                e.Severity.ToString(), e.OccurredAt, e.DetailJson);
        return await query.Take(500).ToListAsync(ct);
    }
}
