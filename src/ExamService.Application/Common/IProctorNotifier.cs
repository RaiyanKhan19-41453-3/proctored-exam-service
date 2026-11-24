namespace ExamService.Application.Common;

public interface IProctorNotifier
{
    Task PublishEventAsync(Guid tenantId, Guid examId, Guid attemptId, object payload, CancellationToken ct = default);
    Task PublishFlagAsync(Guid tenantId, Guid examId, object payload, CancellationToken ct = default);
}

public sealed record AttemptContextInput(string? IpAddress, string? UserAgent,
    string? DeviceFingerprint, string? CountryCode, string? City);
