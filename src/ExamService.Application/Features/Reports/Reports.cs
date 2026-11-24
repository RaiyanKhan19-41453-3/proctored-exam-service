using ExamService.Application.Common;
using ExamService.Domain.Common;
using ExamService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Features.Reports;

/// <summary>
/// Generates one consolidated report per candidate: objective marks, AI-evaluated subjective
/// marks, integrity score and a recommended decision. The examiner reviews exceptions only.
/// </summary>
public sealed record GenerateReport(Guid AttemptId) : IRequest<Guid>, IRequiresTenant;

public sealed class GenerateReportHandler(IAppDbContext db, ITenantContext current, IAiEvaluator ai)
    : IRequestHandler<GenerateReport, Guid>
{
    public async Task<Guid> Handle(GenerateReport request, CancellationToken ct)
    {
        var attempt = await db.Set<ExamAttempt>().FirstOrDefaultAsync(a => a.Id == request.AttemptId, ct)
                      ?? throw new KeyNotFoundException("Attempt not found.");
        var exam = await db.Set<Exam>().FirstOrDefaultAsync(e => e.Id == attempt.ExamId, ct)
                   ?? throw new KeyNotFoundException("Exam not found.");
        var answers = await db.Set<Answer>().Where(a => a.AttemptId == attempt.Id).ToListAsync(ct);
        var questions = await db.Set<Question>().Where(q => q.ExamId == exam.Id)
            .Select(q => new { q.Id, q.Text, q.Marks, q.Type }).ToListAsync(ct);
        var events = await db.Set<ProctorEvent>().Where(e => e.AttemptId == attempt.Id)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new ProctorEventView(e.EventType, e.Severity, e.OccurredAt, e.DetailJson))
            .ToListAsync(ct);

        var subjective = 0m;
        var needsReview = false;
        var feedback = new List<string>();
        if (exam.AiEvaluationEnabled)
        {
            foreach (var answer in answers.Where(a => a.NeedsHumanReview))
            {
                var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                if (question is null) continue;
                var result = await ai.EvaluateAnswerAsync(
                    question.Text, answer.ResponseJson ?? string.Empty, question.Marks, ct);
                answer.MarksAwarded = result.SuggestedMarks;
                answer.Feedback = result.Feedback;
                answer.NeedsHumanReview = result.NeedsHumanReview;
                subjective += result.SuggestedMarks;
                needsReview |= result.NeedsHumanReview;
                feedback.Add(result.Feedback);
            }
        }
        else needsReview = answers.Any(a => a.NeedsHumanReview);

        var integrity = await ai.AssessIntegrityAsync(events, ct);
        var objective = answers.Where(a => !a.NeedsHumanReview).Sum(a => a.MarksAwarded);
        var objectiveMax = questions.Where(q => q.Type != QuestionType.Essay && q.Type != QuestionType.ShortAnswer)
            .Sum(q => q.Marks);
        var subjectiveMax = questions.Where(q => q.Type is QuestionType.Essay or QuestionType.ShortAnswer)
            .Sum(q => q.Marks);

        var report = await db.Set<EvaluationReport>().FirstOrDefaultAsync(r => r.AttemptId == attempt.Id, ct);
        if (report is null)
        {
            report = new EvaluationReport
            {
                TenantId = current.TenantId, AttemptId = attempt.Id, ExamId = exam.Id,
                CandidateId = attempt.CandidateId
            };
            db.Set<EvaluationReport>().Add(report);
        }
        report.ObjectiveScore = objectiveMax == 0 ? 0 : Math.Round(objective / objectiveMax * 100m, 2);
        report.SubjectiveScore = subjectiveMax == 0 ? 0 : Math.Round(subjective / subjectiveMax * 100m, 2);
        report.TotalScore = attempt.MaxScore == 0 ? 0 : Math.Round((objective + subjective) / attempt.MaxScore * 100m, 2);
        report.AiSummary = integrity.Summary + (feedback.Count > 0 ? " | " + string.Join(" ", feedback) : "");
        report.AiRecommendationJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            integrity.Recommendation,
            integrity.RiskScore,
            NeedsHumanReview = needsReview,
            ViolationCount = attempt.ViolationCount
        });
        report.IntegrityScore = integrity.RiskScore;
        report.Decision = needsReview || integrity.ShouldFlag || attempt.Disqualified
            ? ReviewDecision.Flagged : ReviewDecision.Approved;
        attempt.Status = AttemptStatus.Graded;
        attempt.Score = objective + subjective;
        await db.SaveChangesAsync(ct);
        return report.Id;
    }
}

public sealed record ListReports(Guid ExamId) : IRequest<IReadOnlyList<ReportSummaryDto>>, IRequiresTenant;

public sealed record ReportSummaryDto(Guid Id, Guid CandidateId, string CandidateName,
    decimal? TotalScore, decimal IntegrityScore, ReviewDecision Decision, int ViolationCount);

public sealed class ListReportsHandler(IAppDbContext db) : IRequestHandler<ListReports, IReadOnlyList<ReportSummaryDto>>
{
    public async Task<IReadOnlyList<ReportSummaryDto>> Handle(ListReports request, CancellationToken ct)
        => await (from r in db.Set<EvaluationReport>()
                  join a in db.Set<ExamAttempt>() on r.AttemptId equals a.Id
                  where r.ExamId == request.ExamId
                  orderby r.TotalScore descending
                  select new ReportSummaryDto(r.Id, r.CandidateId, a.CandidateName,
                      r.TotalScore, r.IntegrityScore, r.Decision, a.ViolationCount)).ToListAsync(ct);
}

public sealed record ReviewReport(Guid ReportId, ReviewDecision Decision, string? Notes, Guid? Reviewer)
    : IRequest<bool>, IRequiresTenant;

public sealed class ReviewReportHandler(IAppDbContext db, ITenantContext current)
    : IRequestHandler<ReviewReport, bool>
{
    public async Task<bool> Handle(ReviewReport request, CancellationToken ct)
    {
        var report = await db.Set<EvaluationReport>().FirstOrDefaultAsync(r => r.Id == request.ReportId, ct)
                     ?? throw new KeyNotFoundException("Report not found.");
        report.Decision = request.Decision;
        report.ReviewerNotes = request.Notes;
        report.ReviewedBy = request.Reviewer ?? current.UserId;
        report.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record GetReport(Guid ReportId) : IRequest<ReportDetailDto?>, IRequiresTenant;

public sealed record ReportDetailDto(Guid Id, Guid CandidateId, decimal? ObjectiveScore,
    decimal? SubjectiveScore, decimal? TotalScore, string? AiSummary, string? AiRecommendationJson,
    decimal IntegrityScore, ReviewDecision Decision, int ViolationCount,
    IReadOnlyList<EventDto> Events);

public sealed record EventDto(EventType Type, Severity Severity, DateTimeOffset At, string? Detail);

public sealed class GetReportHandler(IAppDbContext db) : IRequestHandler<GetReport, ReportDetailDto?>
{
    public async Task<ReportDetailDto?> Handle(GetReport request, CancellationToken ct)
    {
        var report = await db.Set<EvaluationReport>().FirstOrDefaultAsync(r => r.Id == request.ReportId, ct);
        if (report is null) return null;
        var attempt = await db.Set<ExamAttempt>().FirstOrDefaultAsync(a => a.Id == report.AttemptId, ct);
        var events = await db.Set<ProctorEvent>().Where(e => e.AttemptId == report.AttemptId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new EventDto(e.EventType, e.Severity, e.OccurredAt, e.DetailJson)).ToListAsync(ct);
        return new ReportDetailDto(report.Id, report.CandidateId, report.ObjectiveScore,
            report.SubjectiveScore, report.TotalScore, report.AiSummary, report.AiRecommendationJson,
            report.IntegrityScore, report.Decision, attempt?.ViolationCount ?? 0, events);
    }
}
