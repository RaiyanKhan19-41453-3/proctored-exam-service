using ExamService.Application.Common;
using ExamService.Application.Features.Exams;
using ExamService.Application.Features.Identity;
using ExamService.Application.Features.Sessions;
using ExamService.Application.Features.Reports;
using ExamService.Application.Features.Sessions;
using ExamService.Domain.Common;
using ExamService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ExamService.Tests.Integration;

public class ExamFlowTests(DbFixture fixture) : ExamTestBase(fixture)
{
    private async Task<string> AssignAsync(Guid examId, Guid candidateId)
    {
        var result = await Sender.Send(new CreateAssignment(examId, candidateId, "Jane", null, "photo-key"));
        await Sender.Send(new AcceptConsent(result.AssignmentId, true, "127.0.0.1"));
        var assignment = await Db.Assignments.SingleAsync(a => a.Id == result.AssignmentId);
        assignment.IdentityVerifiedAt = DateTimeOffset.UtcNow; // simulate verified identity
        await Db.SaveChangesAsync();
        return result.AttemptToken;
    }

    private async Task<(Guid ExamId, Guid McqId, Guid EssayId)> BuildExamAsync()
    {
        var examId = await Sender.Send(new CreateExam("Exam", null, null, null, 30, 10,
            true, true, true, true, true, null));
        var mcq = await Sender.Send(new AddQuestion(examId, QuestionType.SingleChoice, "2+2?",
            "[{\"id\":\"a\",\"text\":\"3\"},{\"id\":\"b\",\"text\":\"4\"}]", "{\"value\":\"b\"}", 5m, 1));
        var essay = await Sender.Send(new AddQuestion(examId, QuestionType.Essay, "Explain calculus.",
            null, null, 5m, 2));
        await Sender.Send(new PublishExam(examId));
        return (examId, mcq, essay);
    }

    [Fact]
    public async Task FullFlow_ObjectiveAutoGradingAndReport()
    {
        var (examId, mcqId, _) = await BuildExamAsync();
        var token = await AssignAsync(examId, Guid.NewGuid());
        var attemptId = await Sender.Send(new StartAttempt(examId, token));
        await Sender.Send(new RecordProctorEvent(attemptId, EventType.Copy, Severity.High, "{}", null));
        await Sender.Send(new SaveAnswer(attemptId, mcqId, "{\"value\":\"b\"}"));
        await Sender.Send(new SubmitAttempt(attemptId));

        var reportId = await Sender.Send(new GenerateReport(attemptId));
        var report = await Db.Reports.SingleAsync(r => r.Id == reportId);
        Assert.Equal(50m, report.TotalScore);       // 5/10 — essay needs review
        Assert.True(report.IntegrityScore > 0);
        Assert.Equal(ReviewDecision.Flagged, report.Decision);
    }

    [Fact]
    public async Task CannotStartDraftExam()
    {
        var examId = await Sender.Send(new CreateExam("Draft", null, null, null, 10, 5,
            false, false, false, false, false, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sender.Send(new StartAttempt(examId, "invalid-token")));
    }

    [Fact]
    public async Task DuplicateEventIsRecordedAndCounted()
    {
        var (examId, _, _) = await BuildExamAsync();
        var token = await AssignAsync(examId, Guid.NewGuid());
        var attemptId = await Sender.Send(new StartAttempt(examId, token));
        await Sender.Send(new RecordProctorEvent(attemptId, EventType.TabBlur, Severity.Medium, null, null));
        await Sender.Send(new RecordProctorEvent(attemptId, EventType.TabBlur, Severity.Medium, null, null));
        var attempt = await Db.Attempts.SingleAsync(a => a.Id == attemptId);
        Assert.Equal(2, attempt.ViolationCount);
        Assert.Equal(2, await Db.ProctorEvents.CountAsync(e => e.AttemptId == attemptId));
    }

    [Fact]
    public async Task TenantIsolation_QueriesAreScoped()
    {
        var (examId, _, _) = await BuildExamAsync();
        Assert.Single(await Db.Exams.ToListAsync());
        Tenant.TenantId = Guid.NewGuid();
        Assert.Empty(await Db.Exams.ToListAsync());
    }

    [Fact]
    public async Task CannotStartWithoutVerifiedIdentityAndConsent()
    {
        var (examId, _, _) = await BuildExamAsync();
        var assignment = await Sender.Send(new CreateAssignment(examId, Guid.NewGuid(), "No Consent", null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sender.Send(new StartAttempt(examId, assignment.AttemptToken)));
    }

    [Fact]
    public async Task ExpireAttempts_AutoSubmitsAtDeadline()
    {
        var (examId, _, _) = await BuildExamAsync();
        var token = await AssignAsync(examId, Guid.NewGuid());
        var attemptId = await Sender.Send(new StartAttempt(examId, token));
        var attempt = await Db.Attempts.SingleAsync(a => a.Id == attemptId);
        attempt.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-31);
        await Db.SaveChangesAsync();

        var expired = await Sender.Send(new ExpireAttempts());
        Assert.True(expired >= 1);
        var updated = await Db.Attempts.SingleAsync(a => a.Id == attemptId);
        Assert.Equal(AttemptStatus.UnderReview, updated.Status);
        Assert.NotNull(updated.SubmittedAt);
    }
}
