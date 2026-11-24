using ExamService.Application.Common;
using ExamService.Application.DTOs;
using ExamService.Application.Features.Exams;
using ExamService.Application.Features.Reports;
using ExamService.Application.Features.Sessions;
using MediatR;

namespace ExamService.Application.Features;

public interface IExamService
{
    Task<Guid> CreateAsync(CreateExamRequest request, CancellationToken ct = default);
    Task<bool> PublishAsync(Guid examId, CancellationToken ct = default);
    Task<Guid> AddQuestionAsync(Guid examId, AddQuestionRequest request, CancellationToken ct = default);
    Task<ExamResponse?> GetAsync(Guid examId, CancellationToken ct = default);
}

public interface IAttemptService
{
    Task<Guid> StartAsync(Guid examId, StartAttemptRequest request, CancellationToken ct = default);
    Task<bool> SaveAnswerAsync(Guid attemptId, SaveAnswerRequest request, CancellationToken ct = default);
    Task<Guid> SubmitAsync(Guid attemptId, CancellationToken ct = default);
    Task<bool> RecordEventAsync(Guid attemptId, ProctorEventRequest request, CancellationToken ct = default);
    Task<UploadUrlResponse> GetSnapshotUploadUrlAsync(Guid attemptId, UploadUrlRequest request, CancellationToken ct = default);
}

public interface IReportService
{
    Task<Guid> GenerateAsync(Guid attemptId, CancellationToken ct = default);
    Task<IReadOnlyList<ReportSummary>> ListAsync(Guid examId, CancellationToken ct = default);
    Task<ReportDetail?> GetAsync(Guid reportId, CancellationToken ct = default);
    Task<bool> ReviewAsync(Guid reportId, ReviewReportRequest request, CancellationToken ct = default);
}

public sealed class ExamService(ISender sender) : IExamService
{
    public Task<Guid> CreateAsync(CreateExamRequest r, CancellationToken ct)
        => sender.Send(new CreateExam(r.Title, r.Description, r.StartUtc, r.EndUtc, r.DurationMinutes,
            r.TotalMarks, r.RequireCamera, r.RequireMicrophone, r.LockTab, r.DisableCopyPaste,
            r.AiEvaluationEnabled, r.Instructions), ct);
    public Task<bool> PublishAsync(Guid examId, CancellationToken ct)
        => sender.Send(new PublishExam(examId), ct);
    public Task<Guid> AddQuestionAsync(Guid examId, AddQuestionRequest r, CancellationToken ct)
        => sender.Send(new AddQuestion(examId, r.Type, r.Text, r.OptionsJson, r.CorrectAnswerJson, r.Marks, r.Order), ct);
    public async Task<ExamResponse?> GetAsync(Guid examId, CancellationToken ct)
    {
        var e = await sender.Send(new GetExam(examId), ct);
        return e is null ? null : new ExamResponse(e.Id, e.Title, e.Description, e.Status, e.StartUtc,
            e.EndUtc, e.DurationMinutes, e.TotalMarks, e.RequireCamera, e.RequireMicrophone,
            e.LockTab, e.DisableCopyPaste, e.Instructions,
            e.Questions.Select(q => new QuestionResponse(q.Id, q.Type, q.Text, q.OptionsJson, q.Marks)).ToList());
    }
}

public sealed class AttemptService(ISender sender, IObjectStorage storage, ITenantContext tenant) : IAttemptService
{
    public Task<Guid> StartAsync(Guid examId, StartAttemptRequest r, CancellationToken ct)
        => sender.Send(new StartAttempt(examId, r.CandidateId, r.CandidateName), ct);
    public Task<bool> SaveAnswerAsync(Guid attemptId, SaveAnswerRequest r, CancellationToken ct)
        => sender.Send(new SaveAnswer(attemptId, r.QuestionId, r.ResponseJson), ct);
    public Task<Guid> SubmitAsync(Guid attemptId, CancellationToken ct)
        => sender.Send(new SubmitAttempt(attemptId), ct);
    public Task<bool> RecordEventAsync(Guid attemptId, ProctorEventRequest r, CancellationToken ct)
        => sender.Send(new RecordProctorEvent(attemptId, r.Type, r.Severity, r.DetailJson, r.SnapshotKey), ct);
    public async Task<UploadUrlResponse> GetSnapshotUploadUrlAsync(Guid attemptId, UploadUrlRequest r, CancellationToken ct)
    {
        var key = $"{tenant.TenantId:N}/attempts/{attemptId:N}/{Guid.NewGuid():N}-{r.FileName}";
        var url = await storage.PresignUploadAsync(key, r.ContentType, TimeSpan.FromMinutes(10), ct);
        return new UploadUrlResponse(url, key, DateTimeOffset.UtcNow.AddMinutes(10));
    }
}

public sealed class ReportService(ISender sender) : IReportService
{
    public Task<Guid> GenerateAsync(Guid attemptId, CancellationToken ct)
        => sender.Send(new GenerateReport(attemptId), ct);
    public async Task<IReadOnlyList<ReportSummary>> ListAsync(Guid examId, CancellationToken ct)
    {
        var rows = await sender.Send(new ListReports(examId), ct);
        return rows.Select(r => new ReportSummary(r.Id, r.CandidateId, r.CandidateName,
            r.TotalScore, r.IntegrityScore, r.Decision, r.ViolationCount)).ToList();
    }
    public async Task<ReportDetail?> GetAsync(Guid reportId, CancellationToken ct)
    {
        var r = await sender.Send(new GetReport(reportId), ct);
        return r is null ? null : new ReportDetail(r.Id, r.CandidateId, r.ObjectiveScore, r.SubjectiveScore,
            r.TotalScore, r.AiSummary, r.AiRecommendationJson, r.IntegrityScore, r.Decision,
            r.ViolationCount, r.Events.Select(e => new EventItem(e.Type, e.Severity, e.At, e.Detail)).ToList());
    }
    public Task<bool> ReviewAsync(Guid reportId, ReviewReportRequest r, CancellationToken ct)
        => sender.Send(new ReviewReport(reportId, r.Decision, r.Notes, r.Reviewer), ct);
}
