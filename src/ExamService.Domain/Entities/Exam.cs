using ExamService.Domain.Common;

namespace ExamService.Domain.Entities;

public sealed class Exam : TenantEntity
{
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public int DurationMinutes { get; set; }
    public decimal TotalMarks { get; set; }
    public bool RequireCamera { get; set; } = true;
    public bool RequireMicrophone { get; set; } = true;
    public bool LockTab { get; set; } = true;
    public bool DisableCopyPaste { get; set; } = true;
    public bool AiEvaluationEnabled { get; set; } = true;
    public string? Instructions { get; set; }
}

public sealed class Question : TenantEntity
{
    public Guid ExamId { get; set; }
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;
    public string Text { get; set; } = default!;
    public string? OptionsJson { get; set; }
    public string? CorrectAnswerJson { get; set; }
    public decimal Marks { get; set; } = 1m;
    public int Order { get; set; }
}

public sealed class ExamAttempt : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = default!;
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal? Score { get; set; }
    public decimal MaxScore { get; set; }
    public bool Disqualified { get; set; }
    public string? DisqualificationReason { get; set; }
    public int ViolationCount { get; set; }
}

public sealed class Answer : TenantEntity
{
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public string? ResponseJson { get; set; }
    public bool? IsCorrect { get; set; }
    public decimal MarksAwarded { get; set; }
    public string? Feedback { get; set; }
    public bool NeedsHumanReview { get; set; }
}

public sealed class ProctorEvent : TenantEntity
{
    public Guid AttemptId { get; set; }
    public EventType EventType { get; set; }
    public Severity Severity { get; set; }
    public string? DetailJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? SnapshotKey { get; set; }
    public string? AudioKey { get; set; }
}

public sealed class Recording : TenantEntity
{
    public Guid AttemptId { get; set; }
    public RecordingKind Kind { get; set; }
    public string StorageKey { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Checksum { get; set; }
}

public enum RecordingKind { Camera, Microphone, Screen, Snapshot }

public sealed class EvaluationReport : TenantEntity
{
    public Guid AttemptId { get; set; }
    public Guid ExamId { get; set; }
    public Guid CandidateId { get; set; }
    public decimal? ObjectiveScore { get; set; }
    public decimal? SubjectiveScore { get; set; }
    public decimal? TotalScore { get; set; }
    public string? AiSummary { get; set; }
    public string? AiRecommendationJson { get; set; }
    public decimal IntegrityScore { get; set; }
    public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;
    public string? ReviewerNotes { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class OutboxMessage : Entity
{
    public Guid? TenantId { get; set; }
    public string EventType { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}

public sealed class ExamAssignment : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid CandidateId { get; set; }
    public string CandidateName { get; set; } = default!;
    public string? CandidateEmail { get; set; }
    public string? IdentityPhotoKey { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Assigned;
    public string? AttemptTokenHash { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public DateTimeOffset? IdentityVerifiedAt { get; set; }
    public decimal? FaceMatchScore { get; set; }
    public ConsentStatus Consent { get; set; } = ConsentStatus.Pending;
    public DateTimeOffset? ConsentAt { get; set; }
    public string? ConsentIp { get; set; }
}

public sealed class Evidence : TenantEntity
{
    public Guid AttemptId { get; set; }
    public EvidenceKind Kind { get; set; }
    public string StorageKey { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? Checksum { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? DetailJson { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
