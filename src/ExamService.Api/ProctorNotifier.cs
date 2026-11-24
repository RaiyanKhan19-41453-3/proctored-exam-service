using ExamService.Application.Common;
using Microsoft.AspNetCore.SignalR;

namespace ExamService.Api;

public sealed class ProctorNotifier(IHubContext<ProctorHub> hub) : IProctorNotifier
{
    public Task PublishEventAsync(Guid tenantId, Guid examId, Guid attemptId, object payload, CancellationToken ct)
        => Task.WhenAll(
            hub.Clients.Group($"exam:{examId}").SendAsync("proctor-event", payload, ct),
            hub.Clients.Group($"attempt:{attemptId}").SendAsync("proctor-event", payload, ct));

    public Task PublishFlagAsync(Guid tenantId, Guid examId, object payload, CancellationToken ct)
        => hub.Clients.Group($"exam:{examId}").SendAsync("anomaly-flag", payload, ct);
}
