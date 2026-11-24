using CoachingPlatform.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ExamService.Api;

[Authorize]
public sealed class ProctorHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User?.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tenant))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenant}");
        var examId = Context.GetHttpContext()?.Request.Query["examId"].ToString();
        if (Guid.TryParse(examId, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"exam:{id}");
        var attemptId = Context.GetHttpContext()?.Request.Query["attemptId"].ToString();
        if (Guid.TryParse(attemptId, out var aid))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"attempt:{aid}");
        await base.OnConnectedAsync();
    }
}
