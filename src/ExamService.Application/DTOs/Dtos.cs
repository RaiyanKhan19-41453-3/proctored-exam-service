using ExamService.Domain.Common;

namespace ExamService.Application.DTOs;

public sealed record CreateExamRequest(string Title, string? Description, DateTimeOffset? StartUtc,
    DateTimeOffset? EndUtc, int DurationMinutes, decimal TotalMarks, bool RequireCamera = true,
    bool RequireMicrophone = true, bool LockTab = true, bool DisableCopyPaste = true,
    bool AiEvaluationEnabled = true, string? Instructions = null, bool RequireIdentityVerification = false);

public sealed record AddQuestionRequest(QuestionType Type, string Text, string? OptionsJson,
    string? CorrectAnswerJson, decimal Marks, int Order);

public sealed record QuestionResponse(Guid Id, QuestionType Type, string Text,
    string? OptionsJson, decimal Marks);

public sealed record ExamResponse(Guid Id, string Title, string? Description, ExamStatus Status,
    DateTimeOffset? StartUtc, DateTimeOffset? EndUtc, int DurationMinutes, decimal TotalMarks,
    bool RequireCamera, bool RequireMicrophone, bool LockTab, bool DisableCopyPaste,
    string? Instructions, IReadOnlyList<QuestionResponse> Questions);

public sealed record StartAttemptRequest(Guid CandidateId, string CandidateName);

public sealed record SaveAnswerRequest(Guid QuestionId, string? ResponseJson);

public sealed record ProctorEventRequest(EventType Type, Severity Severity,
    string? DetailJson, string? SnapshotKey);

public sealed record ReportSummary(Guid Id, Guid CandidateId, string CandidateName,
    decimal? TotalScore, decimal IntegrityScore, ReviewDecision Decision, int ViolationCount);

public sealed record ReviewReportRequest(ReviewDecision Decision, string? Notes, Guid? Reviewer);

public sealed record ReportDetail(Guid Id, Guid CandidateId, decimal? ObjectiveScore,
    decimal? SubjectiveScore, decimal? TotalScore, string? AiSummary, string? AiRecommendationJson,
    decimal IntegrityScore, ReviewDecision Decision, int ViolationCount,
    IReadOnlyList<EventItem> Events);

public sealed record EventItem(EventType Type, Severity Severity, DateTimeOffset At, string? Detail);

public sealed record UploadUrlRequest(string FileName, string ContentType, long SizeBytes);
public sealed record UploadUrlResponse(string Url, string Key, DateTimeOffset ExpiresAt);
public sealed record StoreEvidenceRequest(EvidenceKind Kind, string ContentType, long SizeBytes,
    string? Checksum, string? DetailJson, string DataBase64);
public sealed record EvidenceResponse(Guid Id, EvidenceKind Kind, string Url, DateTimeOffset CapturedAt);
public sealed record AttemptTokenRequest(string Token, string? IpAddress = null, ClientMode ClientMode = ClientMode.Browser, string? AttestationJson = null);
public sealed record FaceEventRequest(byte[] Frame);
public sealed record FlagResponse(Guid Id, FlagKind Kind, decimal Score, string? DetailJson, bool Reviewed);

public sealed record ResultResponse(Guid ReportId, decimal? TotalScore, decimal IntegrityScore,
    ReviewDecision Decision, bool Published, string? Summary);
public sealed record PublishResultsResponse(int Published);
