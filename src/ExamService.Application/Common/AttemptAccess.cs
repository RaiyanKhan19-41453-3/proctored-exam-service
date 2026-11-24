using ExamService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Common;

public static class AttemptAccess
{
    public static async Task<ExamAttempt> OwnedAsync(
        this IAppDbContext db, ITenantContext current, Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await db.Set<ExamAttempt>().FirstOrDefaultAsync(a => a.Id == attemptId, ct)
                      ?? throw new AppException("Attempt not found.", 404);
        var privileged = current.Roles.Any(r => r is "Admin" or "Examiner");
        if (!privileged && current.UserId.HasValue && attempt.CandidateId != current.UserId.Value)
            throw new AppException("Not your attempt.", 403);
        return attempt;
    }
}
