using System.Text.Json;
using ExamService.Application.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ExamService.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamService.Application.Features.Reports;

/// <summary>
/// Bridges published results to the main platform's notification API via a configured webhook.
/// The outbox makes delivery retryable; an empty webhook simply marks events processed.
/// </summary>
public sealed record DispatchResultNotifications() : IRequest<int>;

public sealed class DispatchResultNotificationsHandler(
    IAppDbContext db, IConfiguration configuration, HttpClient http, ILogger<DispatchResultNotificationsHandler> logger)
    : IRequestHandler<DispatchResultNotifications, int>
{
    public async Task<int> Handle(DispatchResultNotifications request, CancellationToken ct)
    {
        var webhook = configuration["Notifications:Webhook"];
        var pending = await db.Set<OutboxMessage>()
            .Where(m => m.EventType == EventTypes.ResultsPublished && m.ProcessedAt == null && m.RetryCount < 5)
            .Take(50).ToListAsync(ct);
        foreach (var message in pending)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(webhook))
                {
                    using var doc = JsonDocument.Parse(message.PayloadJson);
                    var content = new StringContent(message.PayloadJson, System.Text.Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));
                    var response = await http.PostAsync(webhook, content, ct);
                    response.EnsureSuccessStatusCode();
                    logger.LogInformation("Delivered result notification for exam {ExamId}",
                        doc.RootElement.GetProperty("ExamId").GetGuid());
                }
                message.ProcessedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.LastError = ex.Message[..Math.Min(ex.Message.Length, 500)];
            }
        }
        if (pending.Count > 0) await db.SaveChangesAsync(ct);
        return pending.Count(m => m.ProcessedAt.HasValue);
    }
}
