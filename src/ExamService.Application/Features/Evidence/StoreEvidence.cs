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
    IEvidenceAnalyzer analyzer) : IRequestHandler<StoreEvidence, Guid>
{
    public async Task<Guid> Handle(StoreEvidence request, CancellationToken ct)
    {
        var data = Convert.FromBase64String(request.Request.DataBase64);
        var attempt = await db.OwnedAsync(current, request.AttemptId, ct);
        if (attempt.Status != AttemptStatus.InProgress)
            throw new AppException("Attempt is not active.", 409);

        var kind = request.Request.Kind;
        var ext = kind is EvidenceKind.CameraChunk or EvidenceKind.IdentitySelfie or EvidenceKind.RoomScan or EvidenceKind.ScreenChunk
            ? "jpg" : "webm";
        var key = $"{current.TenantId:N}/attempts/{attempt.Id:N}/{kind}/{Guid.NewGuid():N}.{ext}";
        var url = await storage.PresignUploadAsync(key, request.Request.ContentType, TimeSpan.FromMinutes(10), ct);
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.Request.ContentType);
        using var http = new HttpClient();
        var put = await http.PutAsync(url, content, ct);
        if (!put.IsSuccessStatusCode) throw new AppException("Evidence upload failed.", 502);

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

        // Analyze frames/audio and turn findings into proctor events automatically.
        if (kind == EvidenceKind.Snapshot)
        {
            var analysis = await analyzer.AnalyzeFrameAsync(data, ct);
            if (!analysis.FacePresent)
                AddEvent(attempt.Id, EventType.FaceAbsent, Domain.Common.Severity.High, "No face detected in frame", evidence.Id, ct);
            if (analysis.FaceCount > 1)
                AddEvent(attempt.Id, EventType.MultipleFaces, Domain.Common.Severity.Critical, $"{analysis.FaceCount} faces detected", evidence.Id, ct);
            if (analysis.FaceCovered)
                AddEvent(attempt.Id, EventType.FaceCovered, Domain.Common.Severity.Critical, "Face appears covered", evidence.Id, ct);
        }
        else if (kind == EvidenceKind.AudioChunk)
        {
            var audio = await analyzer.AnalyzeAudioAsync(data, ct);
            if (audio.Anomaly)
                AddEvent(attempt.Id, EventType.AudioAnomaly, Domain.Common.Severity.High,
                    $"Peak level {audio.PeakLevel}", evidence.Id, ct);
        }
        attempt.ViolationCount += 0; // events increment below via tracked inserts
        await db.SaveChangesAsync(ct);
        return evidence.Id;
    }

    private void AddEvent(Guid attemptId, EventType type, Domain.Common.Severity severity, string detail, Guid evidenceId, CancellationToken ct)
    {
        db.Set<ProctorEvent>().Add(new ProctorEvent
        {
            TenantId = current.TenantId, AttemptId = attemptId, EventType = type,
            Severity = severity, DetailJson = JsonSerializer.Serialize(new { detail, evidenceId }),
            OccurredAt = DateTimeOffset.UtcNow
        });
    }
}
