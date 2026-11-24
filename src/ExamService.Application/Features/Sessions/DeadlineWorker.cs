using ExamService.Application.Common;
using ExamService.Domain.Common;
using MediatR;
using ExamService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Sessions;

/// <summary>Server-authoritative deadline: submissions after this are impossible.</summary>
public sealed record ExpireAttempts() : IRequest<int>;

public sealed class ExpireAttemptsHandler(IAppDbContext db) : IRequestHandler<ExpireAttempts, int>
{
    public async Task<int> Handle(ExpireAttempts request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await (from a in db.Set<ExamAttempt>()
                             join e in db.Set<Exam>() on a.ExamId equals e.Id
                             where a.Status == AttemptStatus.InProgress &&
                                   a.StartedAt.AddMinutes(e.DurationMinutes) <= now
                             select a).ToListAsync(ct);
        foreach (var attempt in expired)
        {
            attempt.Status = AttemptStatus.UnderReview;
            attempt.SubmittedAt = now;
            attempt.DisqualificationReason = "Deadline expired; auto-submitted.";
        }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
