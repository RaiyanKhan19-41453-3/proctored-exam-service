using ExamService.Application.Common;
using ExamService.Application.Features.Exams;
using ExamService.Application.Features.Identity;
using ExamService.Application.Features.Sessions;
using ExamService.Application.Features.Anomalies;
using ExamService.Application.Features.Reports;
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

    [Fact]
    public async Task Randomization_CreatesPerAttemptQuestionOrder()
    {
        var (examId, _, _) = await BuildExamAsync();
        var token = await AssignAsync(examId, Guid.NewGuid());
        var attemptId = await Sender.Send(new StartAttempt(examId, token));
        var links = await Db.AttemptQuestions.Where(a => a.AttemptId == attemptId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(new[] { 1, 2 }, links.Select(l => l.Order).OrderBy(x => x));
        var questions = await Sender.Send(new GetAttemptQuestions(attemptId));
        Assert.Equal(2, questions.Count);
    }

    [Fact]
    public async Task Anomalies_DetectSharedDeviceAndSimilarAnswers()
    {
        var (examId, mcqId, _) = await BuildExamAsync();
        var device = "device-abc";
        var a1 = await Sender.Send(new StartAttempt(examId, await AssignAsync(examId, Guid.NewGuid())));
        var a2 = await Sender.Send(new StartAttempt(examId, await AssignAsync(examId, Guid.NewGuid())));
        foreach (var id in new[] { a1, a2 })
        {
            await Sender.Send(new RecordAttemptContext(id, new AttemptContextInput(
                "10.0.0.1", "UA", device, "US", "X")));
            await Sender.Send(new SaveAnswer(id, mcqId, "{\"value\":\"b\"}"));
            await Sender.Send(new SubmitAttempt(id));
        }

        var count = await Sender.Send(new DetectAnomalies(examId));
        Assert.True(count >= 2);
        var flags = await Sender.Send(new ListAnomalyFlags(examId));
        Assert.Contains(flags, f => f.Kind == FlagKind.SharedDevice);
        Assert.Contains(flags, f => f.Kind == FlagKind.AnswerSimilarity);
    }

    [Fact]
    public async Task Kiosk_IsOptionalByDefault()
    {
        var (examId, _, _) = await BuildExamAsync(); // RequiresKiosk = false
        var token = await AssignAsync(examId, Guid.NewGuid());
        var id = await Sender.Send(new StartAttempt(examId, token, null, ClientMode.Browser));
        Assert.NotEqual(Guid.Empty, id);

        var exam = await Db.Exams.SingleAsync(e => e.Id == examId);
        exam.RequiresKiosk = true;
        await Db.SaveChangesAsync();
        var token2 = await AssignAsync(examId, Guid.NewGuid());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Sender.Send(new StartAttempt(examId, token2, null, ClientMode.Browser)));
    }

    [Fact]
    public async Task Result_IsHiddenUntilPublished()
    {
        var (examId, mcqId, _) = await BuildExamAsync();
        var token = await AssignAsync(examId, Guid.NewGuid());
        var attemptId = await Sender.Send(new StartAttempt(examId, token));
        await Sender.Send(new SaveAnswer(attemptId, mcqId, "{\"value\":\"b\"}"));
        await Sender.Send(new SubmitAttempt(attemptId));
        await Sender.Send(new GenerateReport(attemptId));

        Assert.Null(await Sender.Send(new GetMyResult(attemptId)));
        await Sender.Send(new PublishResults(examId));
        var result = await Sender.Send(new GetMyResult(attemptId));
        Assert.NotNull(result);
        Assert.True(result.Published);
    }

    [Fact]
    public async Task CandidateCannotAccessAnotherCandidatesAttempt()
    {
        var (examId, _, _) = await BuildExamAsync();
        var id = await Sender.Send(new StartAttempt(examId, await AssignAsync(examId, Guid.NewGuid())));
        Tenant.Roles = [];
        Tenant.UserId = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<AppException>(() => Sender.Send(new GetAttemptQuestions(id)));
        Assert.Equal(403, ex.Status);
    }

    [Fact]
    public async Task IdentityVerificationCanBeDisabledForBrowserExams()
    {
        var examId = await Sender.Send(new CreateExam("Browser exam", null, null, null, 20, 5,
            true, false, true, true, false, null, false));
        var question = await Sender.Send(new AddQuestion(examId, QuestionType.SingleChoice, "Q",
            "[]", "{\"value\":\"a\"}", 5m, 1));
        await Sender.Send(new PublishExam(examId));
        var assignment = await Sender.Send(new CreateAssignment(examId, Guid.NewGuid(), "Browser", null, null));
        await Sender.Send(new AcceptConsent(assignment.AssignmentId, true, "127.0.0.1"));
        var id = await Sender.Send(new StartAttempt(examId, assignment.AttemptToken));
        Assert.NotEqual(Guid.Empty, id);
    }
}
