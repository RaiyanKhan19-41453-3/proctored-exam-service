using ExamService.Application.Common;
using ExamService.Application.DTOs;
using ExamService.Application.Features.Evidence;
using ExamService.Application.Features.Sessions;
using ExamService.Application.Features.Exams;
using ExamService.Application.Features.Anomalies;
using ExamService.Application.Features.Identity;
using ExamService.Application.Features.Reports;
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
    Task<Guid> StartAsync(Guid examId, AttemptTokenRequest request, CancellationToken ct = default);
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
    public Task<Guid> StartAsync(Guid examId, AttemptTokenRequest r, CancellationToken ct)
        => sender.Send(new StartAttempt(examId, r.Token, r.IpAddress, r.ClientMode, r.AttestationJson), ct);
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

public interface IIdentityService
{
    Task<AssignmentResponse> AssignAsync(CreateAssignment request, CancellationToken ct = default);
    Task<bool> AcceptConsentAsync(Guid assignmentId, bool accepted, string? ip, CancellationToken ct = default);
    Task<IdentityVerificationDto> VerifyAsync(Guid assignmentId, byte[] selfie, CancellationToken ct = default);
}

public interface IEvidenceService
{
    Task<Guid> StoreAsync(Guid attemptId, StoreEvidenceRequest request, byte[] data, CancellationToken ct = default);
}

public sealed class IdentityService(ISender sender) : IIdentityService
{
    public Task<AssignmentResponse> AssignAsync(CreateAssignment r, CancellationToken ct) => sender.Send(r, ct);
    public Task<bool> AcceptConsentAsync(Guid id, bool accepted, string? ip, CancellationToken ct)
        => sender.Send(new AcceptConsent(id, accepted, ip), ct);
    public Task<IdentityVerificationDto> VerifyAsync(Guid id, byte[] selfie, CancellationToken ct)
        => sender.Send(new VerifyIdentity(id, selfie), ct);
}

public sealed class EvidenceService(ISender sender) : IEvidenceService
{
    public Task<Guid> StoreAsync(Guid attemptId, StoreEvidenceRequest r, byte[] data, CancellationToken ct)
        => sender.Send(new StoreEvidence(attemptId, r, data), ct);
}

public interface IAnomalyService
{
    Task<bool> RecordContextAsync(Guid attemptId, AttemptContextInput input, CancellationToken ct = default);
    Task<int> DetectAsync(Guid examId, CancellationToken ct = default);
}

public sealed class AnomalyService(ISender sender) : IAnomalyService
{
    public Task<bool> RecordContextAsync(Guid attemptId, AttemptContextInput input, CancellationToken ct)
        => sender.Send(new RecordAttemptContext(attemptId, input), ct);
    public Task<int> DetectAsync(Guid examId, CancellationToken ct)
        => sender.Send(new DetectAnomalies(examId), ct);
}
