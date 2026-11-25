using Microsoft.EntityFrameworkCore;

namespace ExamService.Application.Common;

public interface IAppDbContext
{
    DbSet<T> Set<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<bool> ClaimAttemptTokenAsync(Guid assignmentId, DateTimeOffset now, CancellationToken ct = default);
}

public interface ITenantContext
{
    Guid TenantId { get; }
    Guid? UserId { get; }
    string? UserEmail { get; }
    IReadOnlyList<string> Roles { get; }
}

public interface IRequiresTenant;

public sealed class AppException : Exception
{
    public AppException(string message, int status = 400) : base(message) { Status = status; }
    public int Status { get; }
}

public interface IObjectStorage
{
    Task<string> PresignUploadAsync(string key, string contentType, TimeSpan expiry, CancellationToken ct = default);
    Task<string> PresignDownloadAsync(string key, TimeSpan expiry, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task PutAsync(string key, byte[] data, string contentType, CancellationToken ct = default);
    Task EnsureBucketAsync(CancellationToken ct = default);
}

/// <summary>Abstracts AI-based subjective answer analysis and integrity review.</summary>
public interface IAiEvaluator
{
    Task<AiAnswerEvaluation> EvaluateAnswerAsync(string question, string answer, decimal maxMarks, CancellationToken ct);
    Task<AiIntegrityAssessment> AssessIntegrityAsync(IReadOnlyList<Domain.Common.ProctorEventView> events, CancellationToken ct);
}

public sealed record AiAnswerEvaluation(decimal SuggestedMarks, string Feedback, bool NeedsHumanReview);
public sealed record AiIntegrityAssessment(decimal RiskScore, string Summary, string Recommendation, bool ShouldFlag);

public static class EventTypes
{
    public const string AttemptStarted = "exam.attempt_started";
    public const string AttemptSubmitted = "exam.attempt_submitted";
    public const string ResultsPublished = "exam.results_published";
    public const string ProctorEvent = "exam.proctor_event";
}
