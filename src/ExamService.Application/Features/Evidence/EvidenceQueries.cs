using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Evidence;

public sealed record ListEvidence(Guid AttemptId, EvidenceKind? Kind) : IRequest<IReadOnlyList<EvidenceDto>>, IRequiresTenant;

public sealed record EvidenceDto(Guid Id, EvidenceKind Kind, string StorageKey,
    long SizeBytes, DateTimeOffset CapturedAt, string? DetailJson);

public sealed class ListEvidenceHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<ListEvidence, IReadOnlyList<EvidenceDto>>
{
    public async Task<IReadOnlyList<EvidenceDto>> Handle(ListEvidence request, CancellationToken ct)
    {
        await db.OwnedAsync(current, request.AttemptId, ct);
        var q = db.Set<Domain.Entities.Evidence>().AsNoTracking().Where(e => e.AttemptId == request.AttemptId);
        if (request.Kind.HasValue) q = q.Where(e => e.Kind == request.Kind.Value);
        return await q.OrderBy(e => e.CapturedAt)
            .Select(e => new EvidenceDto(e.Id, e.Kind, e.StorageKey, e.SizeBytes, e.CapturedAt, e.DetailJson))
            .ToListAsync(ct);
    }
}

public sealed record GetEvidenceUrl(Guid EvidenceId) : IRequest<string>, IRequiresTenant;

public sealed class GetEvidenceUrlHandler(IAppDbContext db, IObjectStorage storage, ITenantContext current)
    : IRequestHandler<GetEvidenceUrl, string>
{
    public async Task<string> Handle(GetEvidenceUrl request, CancellationToken ct)
    {
        var evidence = await db.Set<Domain.Entities.Evidence>().FirstOrDefaultAsync(e => e.Id == request.EvidenceId, ct)
                       ?? throw new AppException("Evidence not found.", 404);
        await db.OwnedAsync(current, evidence.AttemptId, ct);
        return await storage.PresignDownloadAsync(evidence.StorageKey, TimeSpan.FromMinutes(30), ct);
    }
}

public sealed record CleanupExpiredEvidence() : IRequest<int>;

public sealed class CleanupExpiredEvidenceHandler(IAppDbContext db)
    : IRequestHandler<CleanupExpiredEvidence, int>
{
    public async Task<int> Handle(CleanupExpiredEvidence request, CancellationToken ct)
    {
        var expired = await db.Set<Domain.Entities.Evidence>().Where(e => e.ExpiresAt != null && e.ExpiresAt < DateTimeOffset.UtcNow)
            .ToListAsync(ct);
        foreach (var item in expired) db.Set<Domain.Entities.Evidence>().Remove(item);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }
}
