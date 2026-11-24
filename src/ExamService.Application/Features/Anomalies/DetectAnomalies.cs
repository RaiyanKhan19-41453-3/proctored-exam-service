using System.Text.Json;
using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Anomalies;

public sealed record RecordAttemptContext(Guid AttemptId, AttemptContextInput Input)
    : IRequest<bool>, IRequiresTenant;

public sealed class RecordAttemptContextHandler(IAppDbContext db, ITenantContext current)
    : IRequestHandler<RecordAttemptContext, bool>
{
    public async Task<bool> Handle(RecordAttemptContext request, CancellationToken ct)
    {
        if (!await db.Set<ExamAttempt>().AnyAsync(a => a.Id == request.AttemptId, ct))
            throw new AppException("Attempt not found.", 404);
        var i = request.Input;
        db.Set<AttemptContextRecord>().Add(new AttemptContextRecord
        {
            TenantId = current.TenantId, AttemptId = request.AttemptId,
            IpAddress = i.IpAddress, UserAgent = i.UserAgent,
            DeviceFingerprint = i.DeviceFingerprint, CountryCode = i.CountryCode,
            City = i.City, CapturedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record DetectAnomalies(Guid ExamId) : IRequest<int>, IRequiresTenant;

public sealed class DetectAnomaliesHandler(IAppDbContext db, ITenantContext current, IProctorNotifier notifier)
    : IRequestHandler<DetectAnomalies, int>
{
    public async Task<int> Handle(DetectAnomalies request, CancellationToken ct)
    {
        var attempts = await db.Set<ExamAttempt>()
            .Where(a => a.ExamId == request.ExamId && a.Status != AttemptStatus.InProgress)
            .Select(a => new { a.Id, a.CandidateId, a.CandidateName }).ToListAsync(ct);
        var contexts = await db.Set<AttemptContextRecord>()
            .Where(c => attempts.Select(a => a.Id).Contains(c.AttemptId)).ToListAsync(ct);
        var answers = await db.Set<Answer>()
            .Where(a => attempts.Select(x => x.Id).Contains(a.AttemptId))
            .Select(a => new AnswerView(a.AttemptId, a.QuestionId, a.ResponseJson)).ToListAsync(ct);
        var created = 0;

        for (var i = 0; i < attempts.Count; i++)
        for (var j = i + 1; j < attempts.Count; j++)
        {
            var a = attempts[i]; var b = attempts[j];
            var ca = contexts.FirstOrDefault(c => c.AttemptId == a.Id);
            var cb = contexts.FirstOrDefault(c => c.AttemptId == b.Id);

            if (ca?.DeviceFingerprint is { } d1 && cb?.DeviceFingerprint == d1)
                created += await Flag(request.ExamId, a.Id, b.Id, FlagKind.SharedDevice, 100m,
                    new { device = d1, a = a.CandidateName, b = b.CandidateName }, ct);
            if (ca?.IpAddress is { } ip && cb?.IpAddress == ip)
                created += await Flag(request.ExamId, a.Id, b.Id, FlagKind.SharedIp, 80m,
                    new { ip, a = a.CandidateName, b = b.CandidateName }, ct);
            if (ca?.CountryCode is { } c1 && cb?.CountryCode is { } c2 && c1 != c2)
                created += await Flag(request.ExamId, a.Id, b.Id, FlagKind.GeoAnomaly, 60m,
                    new { aCountry = c1, bCountry = c2 }, ct);

            var sim = Similarity(answers.Where(x => x.AttemptId == a.Id).ToList(),
                                 answers.Where(x => x.AttemptId == b.Id).ToList());
            if (sim >= 70m)
                created += await Flag(request.ExamId, a.Id, b.Id, FlagKind.AnswerSimilarity, sim,
                    new { a = a.CandidateName, b = b.CandidateName }, ct);
        }
        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<int> Flag(Guid examId, Guid a, Guid b, FlagKind kind, decimal score, object detail, CancellationToken ct)
    {
        var exists = await db.Set<AnomalyFlag>().AnyAsync(f =>
            f.ExamId == examId && f.Kind == kind &&
            ((f.AttemptId == a && f.OtherAttemptId == b) || (f.AttemptId == b && f.OtherAttemptId == a)), ct);
        if (exists) return 0;
        db.Set<AnomalyFlag>().Add(new AnomalyFlag
        {
            TenantId = current.TenantId, ExamId = examId, AttemptId = a,
            OtherAttemptId = b, Kind = kind, Score = score,
            DetailJson = JsonSerializer.Serialize(detail)
        });
        await notifier.PublishFlagAsync(current.TenantId, examId, new
        {
            kind = kind.ToString(), score, attemptA = a, attemptB = b
        }, ct);
        return 1;
    }

    private static decimal Similarity(
        IReadOnlyList<AnswerView> a, IReadOnlyList<AnswerView> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0m;
        var mapA = a.ToDictionary(x => x.QuestionId, x => Normalize(x.ResponseJson));
        var mapB = b.ToDictionary(x => x.QuestionId, x => Normalize(x.ResponseJson));
        var common = mapA.Keys.Intersect(mapB.Keys).ToList();
        if (common.Count == 0) return 0m;
        var same = common.Count(k => mapA[k] == mapB[k] && !string.IsNullOrWhiteSpace(mapA[k]));
        return Math.Round(same * 100m / common.Count, 2);
    }

    private sealed record AnswerView(Guid AttemptId, Guid QuestionId, string? ResponseJson);

    private static string Normalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetRawText().ToLowerInvariant()
                .Replace(" ", "").Replace("\"", "");
        }
        catch (JsonException) { return json.Trim().ToLowerInvariant(); }
    }
}
