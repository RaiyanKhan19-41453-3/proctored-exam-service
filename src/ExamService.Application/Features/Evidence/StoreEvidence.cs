using System.Security.Cryptography;
using System.Text.Json;
using ExamService.Application.Common;
using ExamService.Application.DTOs;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Evidence;

public sealed record StoreEvidence(Guid AttemptId, StoreEvidenceRequest Request, byte[] Data)
    : IRequest<Guid>, IRequiresTenant;

public sealed class StoreEvidenceValidator : AbstractValidator<StoreEvidence>
{
    public StoreEvidenceValidator()
    {
        RuleFor(x => x.AttemptId).NotEmpty();
        RuleFor(x => x.Request.DataBase64).NotEmpty()
            .WithMessage("Evidence chunk must be at most 20 MB.");
    }
}

public sealed class StoreEvidenceHandler(IAppDbContext db, IObjectStorage storage, ITenantContext current,
    IEvidenceAnalyzer analyzer, IProctorNotifier notifier) : IRequestHandler<StoreEvidence, Guid>
{
    public async Task<Guid> Handle(StoreEvidence request, CancellationToken ct)
    {
        var data = Convert.FromBase64String(request.Request.DataBase64);
        var attempt = await db.OwnedAsync(current, request.AttemptId, ct);
        if (attempt.Status != AttemptStatus.InProgress)
            throw new AppException("Attempt is not active.", 409);

        var kind = request.Request.Kind;
        var ext = kind is EvidenceKind.Snapshot or EvidenceKind.CameraChunk or EvidenceKind.IdentitySelfie or EvidenceKind.RoomScan or EvidenceKind.ScreenChunk
            ? "jpg" : "webm";
        var key = $"{current.TenantId:N}/attempts/{attempt.Id:N}/{kind}/{Guid.NewGuid():N}.{ext}";
        await storage.PutAsync(key, data, request.Request.ContentType, ct);

        var checksum = request.Request.Checksum ?? Convert.ToHexString(SHA256.HashData(data));
        var evidence = new Domain.Entities.Evidence
        {
            TenantId = current.TenantId, AttemptId = attempt.Id, Kind = kind,
            StorageKey = key, ContentType = request.Request.ContentType,
            SizeBytes = data.Length, Checksum = checksum,
            CapturedAt = DateTimeOffset.UtcNow,
            DetailJson = request.Request.DetailJson,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(90)
        };
        db.Set<Domain.Entities.Evidence>().Add(evidence);

        var newEventCount = 0;
        // Analyze frames/audio and turn findings into proctor events automatically.
        if (kind == EvidenceKind.Snapshot)
        {
            var analysis = await analyzer.AnalyzeFrameAsync(data, ct);
            if (!analysis.FacePresent)
                if (await AddEvent(attempt, EventType.FaceAbsent, Domain.Common.Severity.High, "No face detected in frame", evidence.Id)) newEventCount++;
            if (analysis.FaceCount > 1)
                if (await AddEvent(attempt, EventType.MultipleFaces, Domain.Common.Severity.Critical, $"{analysis.FaceCount} faces detected", evidence.Id)) newEventCount++;
            if (analysis.FaceCovered)
                if (await AddEvent(attempt, EventType.FaceCovered, Domain.Common.Severity.Critical, "Face appears covered", evidence.Id)) newEventCount++;
        }
        else if (kind == EvidenceKind.AudioChunk &&
                 request.Request.ContentType is "audio/wav" or "audio/pcm" or "audio/x-wav")
        {
            var audio = await analyzer.AnalyzeAudioAsync(data, ct);
            if (audio.Anomaly)
                if (await AddEvent(attempt, EventType.AudioAnomaly, Domain.Common.Severity.High,
                    $"Peak level {audio.PeakLevel}", evidence.Id)) newEventCount++;
        }
        attempt.ViolationCount += newEventCount;
        await db.SaveChangesAsync(ct);
        return evidence.Id;
    }

    private async Task<bool> AddEvent(ExamAttempt attempt, EventType type, Domain.Common.Severity severity,
        string detail, Guid evidenceId)
    {
        var now = DateTimeOffset.UtcNow;
        db.Set<ProctorEvent>().Add(new ProctorEvent
        {
            TenantId = current.TenantId, AttemptId = attempt.Id, EventType = type,
            Severity = severity, DetailJson = JsonSerializer.Serialize(new { detail, evidenceId }),
            OccurredAt = now
        });
        await notifier.PublishEventAsync(current.TenantId, attempt.ExamId, attempt.Id, new
        {
            attemptId = attempt.Id, examId = attempt.ExamId, type = type.ToString(),
            severity = severity.ToString(), at = now, detail
        });
        return true;
    }
}
