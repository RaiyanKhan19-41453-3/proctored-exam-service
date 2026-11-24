using System.Text.Json;
using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using FluentValidation;
using MediatR;
using ExamService.Application.Features.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Sessions;

public sealed record StartAttempt(Guid ExamId, string AttemptToken, string? IpAddress = null,
    ClientMode ClientMode = ClientMode.Browser, string? AttestationJson = null) : IRequest<Guid>, IRequiresTenant;

public sealed class StartAttemptValidator : AbstractValidator<StartAttempt>
{
    public StartAttemptValidator()
    {
        RuleFor(x => x.ExamId).NotEmpty();
        RuleFor(x => x.AttemptToken).NotEmpty();
    }
}

public sealed class StartAttemptHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<StartAttempt, Guid>
{
    public async Task<Guid> Handle(StartAttempt request, CancellationToken ct)
    {
        var exam = await db.Set<Exam>().FirstOrDefaultAsync(e => e.Id == request.ExamId, ct)
                   ?? throw new KeyNotFoundException("Exam not found.");
        if (exam.Status != ExamStatus.Published) throw new InvalidOperationException("Exam is not live.");

        // Identity binding: the token is issued server-side to one assigned candidate.
        var tokenHash = Identity.CreateAssignmentHandler.Hash(request.AttemptToken);
        var assignment = await db.Set<ExamAssignment>().FirstOrDefaultAsync(a =>
            a.ExamId == exam.Id && a.AttemptTokenHash == tokenHash, ct)
            ?? throw new UnauthorizedAccessException("Invalid attempt token.");
        if (assignment.TokenExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Attempt token has expired.");
        var privileged = current.Roles.Any(r => r is "Admin" or "Teacher" or "Examiner");
        if (!privileged && current.UserId.HasValue && assignment.CandidateId != current.UserId.Value)
            throw new UnauthorizedAccessException("This attempt token belongs to a different candidate.");
        if (assignment.Status == AssignmentStatus.Revoked)
            throw new UnauthorizedAccessException("Assignment has been revoked.");
        if (assignment.Consent != ConsentStatus.Accepted)
            throw new InvalidOperationException("Consent must be accepted before starting.");
        if (exam.RequireIdentityVerification && assignment.IdentityVerifiedAt is null)
            throw new InvalidOperationException("Identity must be verified before starting.");
        var now = DateTimeOffset.UtcNow;
        if (exam.StartUtc.HasValue && now < exam.StartUtc.Value) throw new InvalidOperationException("Exam has not started.");
        if (exam.EndUtc.HasValue && now > exam.EndUtc.Value) throw new InvalidOperationException("Exam has ended.");

        if (exam.RequiresKiosk && request.ClientMode != ClientMode.Kiosk)
            throw new UnauthorizedAccessException("This exam requires the secure kiosk client.");

        var existing = await db.Set<ExamAttempt>().FirstOrDefaultAsync(a =>
            a.ExamId == exam.Id && a.CandidateId == assignment.CandidateId &&
            a.Status == AttemptStatus.InProgress, ct);
        if (existing is not null) return existing.Id;

        var questions = await db.Set<Question>().Where(q => q.ExamId == exam.Id)
            .Select(q => new { q.Id, q.Marks }).ToListAsync(ct);
        var attempt = new ExamAttempt
        {
            TenantId = current.TenantId, ExamId = exam.Id, CandidateId = assignment.CandidateId,
            CandidateName = assignment.CandidateName, StartedAt = now, MaxScore = questions.Sum(q => q.Marks),
            ClientMode = request.ClientMode, AttestationJson = request.AttestationJson,
            RandomizationSeed = $"{assignment.Id:N}{Guid.NewGuid():N}"
        };
        // Server-side randomization: order and option order are stored per attempt.
        var rng = new Random(attempt.RandomizationSeed!.GetHashCode());
        var ordered = questions.OrderBy(_ => rng.Next()).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var question = await db.Set<Question>().FirstAsync(q => q.Id == ordered[i].Id, ct);
            db.Set<AttemptQuestion>().Add(new AttemptQuestion
            {
                TenantId = current.TenantId, AttemptId = attempt.Id,
                QuestionId = question.Id, Order = i + 1,
                ShuffledOptionsJson = QuestionShuffler.ShuffleOptions(question.OptionsJson, rng)
            });
        }
        db.Set<ExamAttempt>().Add(attempt);
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            TenantId = current.TenantId, EventType = EventTypes.AttemptStarted,
            PayloadJson = JsonSerializer.Serialize(new { AttemptId = attempt.Id, ExamId = exam.Id, CandidateId = assignment.CandidateId }),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return attempt.Id;
    }
}

public sealed record SaveAnswer(Guid AttemptId, Guid QuestionId, string? ResponseJson)
    : IRequest<bool>, IRequiresTenant;

public sealed class SaveAnswerHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<SaveAnswer, bool>
{
    public async Task<bool> Handle(SaveAnswer request, CancellationToken ct)
    {
        var attempt = await db.OwnedAsync(current, request.AttemptId, ct);
        if (attempt.Status != AttemptStatus.InProgress) throw new InvalidOperationException("Attempt is not active.");
        var question = await db.Set<Question>().FirstOrDefaultAsync(q => q.Id == request.QuestionId && q.ExamId == attempt.ExamId, ct)
                       ?? throw new KeyNotFoundException("Question not found.");
        if (DateTimeOffset.UtcNow > attempt.StartedAt.AddMinutes(
                (await db.Set<Exam>().Where(e => e.Id == attempt.ExamId).Select(e => e.DurationMinutes).FirstAsync(ct)) + 5))
            throw new InvalidOperationException("Time is up.");

        var answer = await db.Set<Answer>().FirstOrDefaultAsync(a =>
            a.AttemptId == attempt.Id && a.QuestionId == question.Id, ct);
        if (answer is null)
        {
            answer = new Answer
            {
                TenantId = current.TenantId, AttemptId = attempt.Id, QuestionId = question.Id
            };
            db.Set<Answer>().Add(answer);
        }
        answer.ResponseJson = request.ResponseJson;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record SubmitAttempt(Guid AttemptId) : IRequest<Guid>, IRequiresTenant;

public sealed class SubmitAttemptHandler(IAppDbContext db, ITenantContext current) : IRequestHandler<SubmitAttempt, Guid>
{
    public async Task<Guid> Handle(SubmitAttempt request, CancellationToken ct)
    {
        var attempt = await db.OwnedAsync(current, request.AttemptId, ct);
        if (attempt.Status != AttemptStatus.InProgress) return attempt.Id;

        var exam = await db.Set<Exam>().FirstOrDefaultAsync(e => e.Id == attempt.ExamId, ct)
                   ?? throw new KeyNotFoundException("Exam not found.");
        var questions = await db.Set<Question>().Where(q => q.ExamId == exam.Id)
            .Select(q => new { q.Id, q.Type, q.CorrectAnswerJson, q.Marks }).ToListAsync(ct);
        var answers = await db.Set<Answer>().Where(a => a.AttemptId == attempt.Id).ToListAsync(ct);

        decimal score = 0;
        foreach (var answer in answers)
        {
            var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
            if (question is null || string.IsNullOrWhiteSpace(question.CorrectAnswerJson) ||
                string.IsNullOrWhiteSpace(answer.ResponseJson))
            {
                answer.NeedsHumanReview = true;
                continue;
            }
            try
            {
                using var expected = JsonDocument.Parse(question.CorrectAnswerJson);
                using var actual = JsonDocument.Parse(answer.ResponseJson);
                var correct = question.Type switch
                {
                    QuestionType.SingleChoice or QuestionType.TrueFalse =>
                        expected.RootElement.GetProperty("value").GetRawText() == actual.RootElement.GetProperty("value").GetRawText(),
                    QuestionType.MultipleChoice =>
                        expected.RootElement.GetProperty("values").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x)
                            .SequenceEqual(actual.RootElement.GetProperty("values").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x)),
                    _ => false
                };
                answer.IsCorrect = correct;
                answer.MarksAwarded = correct ? question.Marks : 0m;
                score += answer.MarksAwarded;
            }
            catch (JsonException) { answer.NeedsHumanReview = true; }
        }

        attempt.Status = AttemptStatus.UnderReview;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;
        attempt.Score = score;
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            TenantId = current.TenantId, EventType = EventTypes.AttemptSubmitted,
            PayloadJson = JsonSerializer.Serialize(new
            { attempt.Id, attempt.ExamId, attempt.CandidateId, attempt.CandidateName, Score = score }),
            OccurredAt = attempt.SubmittedAt.Value
        });
        await db.SaveChangesAsync(ct);
        return attempt.Id;
    }
}

public sealed record RecordProctorEvent(Guid AttemptId, EventType Type, Domain.Common.Severity Severity, string? DetailJson,
    string? SnapshotKey) : IRequest<bool>, IRequiresTenant;

public sealed class RecordProctorEventHandler(IAppDbContext db, ITenantContext current, IProctorNotifier notifier)
    : IRequestHandler<RecordProctorEvent, bool>
{
    public async Task<bool> Handle(RecordProctorEvent request, CancellationToken ct)
    {
        var attempt = await db.OwnedAsync(current, request.AttemptId, ct);
        db.Set<ProctorEvent>().Add(new ProctorEvent
        {
            TenantId = current.TenantId, AttemptId = attempt.Id, EventType = request.Type,
            Severity = request.Severity, DetailJson = request.DetailJson,
            SnapshotKey = request.SnapshotKey, OccurredAt = DateTimeOffset.UtcNow
        });
        attempt.ViolationCount++;
        await db.SaveChangesAsync(ct);
        await notifier.PublishEventAsync(current.TenantId, attempt.ExamId, attempt.Id, new
        {
            attemptId = attempt.Id, examId = attempt.ExamId,
            type = request.Type.ToString(), severity = request.Severity.ToString(),
            at = DateTimeOffset.UtcNow, detail = request.DetailJson
        }, ct);
        return true;
    }
}

public static class QuestionShuffler
{
    public static string? ShuffleOptions(string? optionsJson, Random rng)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var list = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return JsonSerializer.Serialize(list);
        }
        catch (JsonException) { return optionsJson; }
    }
}
