using ExamService.Application.DTOs;
using ExamService.Application.Features;
using ExamService.Application.Features.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExamService.Application.Common;
using ExamService.Domain.Common;

namespace ExamService.Api;

[ApiController]
[Route("api/exams")]
[Authorize(Roles = "Admin,Teacher,Examiner")]
[Produces("application/json")]
public sealed class ExamsController(IExamService exams) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExamRequest request, CancellationToken ct)
        => Ok(new { id = await exams.CreateAsync(request, ct) });

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish([FromRoute] Guid id, CancellationToken ct)
        => Ok(new { published = await exams.PublishAsync(id, ct) });

    [HttpPost("{id:guid}/questions")]
    public async Task<IActionResult> AddQuestion([FromRoute] Guid id,
        [FromBody] AddQuestionRequest request, CancellationToken ct)
        => Ok(new { questionId = await exams.AddQuestionAsync(id, request, ct) });

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
    {
        var exam = await exams.GetAsync(id, ct);
        return exam is null ? NotFound() : Ok(exam);
    }
}

[ApiController]
[Route("api/attempts")]
[Authorize]
[Produces("application/json")]
public sealed class AttemptsController(IAttemptService attempts, MediatR.ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start([FromQuery] Guid examId,
        [FromBody] AttemptTokenRequest request, CancellationToken ct)
        => Ok(new { attemptId = await attempts.StartAsync(examId, request, ct) });

    [HttpPost("{id:guid}/answers")]
    public async Task<IActionResult> SaveAnswer([FromRoute] Guid id,
        [FromBody] SaveAnswerRequest request, CancellationToken ct)
        => Ok(new { saved = await attempts.SaveAnswerAsync(id, request, ct) });

    [HttpGet("{id:guid}/questions")]
    public async Task<IActionResult> Questions([FromRoute] Guid id, CancellationToken ct)
        => Ok(await sender.Send(new GetAttemptQuestions(id), ct));

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit([FromRoute] Guid id, CancellationToken ct)
        => Ok(new { attemptId = await attempts.SubmitAsync(id, ct) });

    [HttpPost("{id:guid}/events")]
    public async Task<IActionResult> RecordEvent([FromRoute] Guid id,
        [FromBody] ProctorEventRequest request, CancellationToken ct)
        => Ok(new { recorded = await attempts.RecordEventAsync(id, request, ct) });

    [HttpPost("{id:guid}/snapshots/upload-url")]
    public async Task<IActionResult> SnapshotUrl([FromRoute] Guid id,
        [FromBody] UploadUrlRequest request, CancellationToken ct)
        => Ok(await attempts.GetSnapshotUploadUrlAsync(id, request, ct));
}

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin,Teacher,Examiner")]
[Produces("application/json")]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromQuery] Guid attemptId, CancellationToken ct)
        => Ok(new { reportId = await reports.GenerateAsync(attemptId, ct) });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid examId, CancellationToken ct)
        => Ok(await reports.ListAsync(examId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
    {
        var report = await reports.GetAsync(id, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpPost("exams/{examId:guid}/publish")]
    public async Task<IActionResult> Publish([FromRoute] Guid examId, [FromQuery] bool includeFlagged = false, CancellationToken ct = default)
        => Ok(new { published = await reports.PublishAsync(examId, includeFlagged, ct) });

    [HttpGet("attempts/{attemptId:guid}/result")]
    [Authorize]
    public async Task<IActionResult> MyResult([FromRoute] Guid attemptId, CancellationToken ct)
    {
        var result = await reports.GetResultAsync(attemptId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review([FromRoute] Guid id,
        [FromBody] ReviewReportRequest request, CancellationToken ct)
        => Ok(new { reviewed = await reports.ReviewAsync(id, request, ct) });
}

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { service = "proctored-exam-service", status = "running" });
}

[ApiController]
[Route("api/candidates")]
[Authorize(Roles = "Admin,Teacher,Examiner")]
[Produces("application/json")]
public sealed class CandidatesController(IIdentityService identity) : ControllerBase
{
    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromBody] ExamService.Application.Features.Identity.CreateAssignment request, CancellationToken ct)
        => Ok(await identity.AssignAsync(request, ct));

}


[ApiController]
[Route("api/candidate")]
[Authorize]
[Produces("application/json")]
public sealed class CandidateSelfServiceController(IIdentityService identity) : ControllerBase
{
    [HttpPost("consent")]
    [Authorize]
    public async Task<IActionResult> Consent([FromQuery] Guid assignmentId, [FromQuery] bool accepted,
        CancellationToken ct)
        => Ok(new { accepted = await identity.AcceptConsentAsync(assignmentId, accepted,
            HttpContext.Connection.RemoteIpAddress?.ToString(), ct) });

    [HttpPost("verify")]
    [Authorize]
    public async Task<IActionResult> Verify([FromQuery] Guid assignmentId, [FromBody] byte[] selfie, CancellationToken ct)
        => Ok(await identity.VerifyAsync(assignmentId, selfie, ct));
}

[ApiController]
[Route("api/evidence")]
[Authorize]
[Produces("application/json")]
public sealed class EvidenceController(MediatR.ISender sender) : ControllerBase
{
    [HttpPost("attempts/{attemptId:guid}")]
    public async Task<IActionResult> Store([FromRoute] Guid attemptId,
        [FromBody] ExamService.Application.DTOs.StoreEvidenceRequest request, CancellationToken ct)
        => Ok(new { evidenceId = await sender.Send(new ExamService.Application.Features.Evidence.StoreEvidence(
            attemptId, request, []), ct) });

    [HttpGet("attempts/{attemptId:guid}")]
    public async Task<IActionResult> List([FromRoute] Guid attemptId,
        [FromQuery] ExamService.Domain.Common.EvidenceKind? kind, CancellationToken ct)
        => Ok(await sender.Send(new ExamService.Application.Features.Evidence.ListEvidence(attemptId, kind), ct));

    [HttpGet("{id:guid}/url")]
    public async Task<IActionResult> GetUrl([FromRoute] Guid id, CancellationToken ct)
        => Ok(new { url = await sender.Send(new ExamService.Application.Features.Evidence.GetEvidenceUrl(id), ct) });
}

[ApiController]
[Route("api/proctor")]
[Authorize(Roles = "Admin,Teacher,Examiner")]
[Produces("application/json")]
public sealed class ProctorController(IAnomalyService anomalies, MediatR.ISender sender) : ControllerBase
{
    [HttpPost("attempts/{attemptId:guid}/context")]
    public async Task<IActionResult> Context([FromRoute] Guid attemptId,
        [FromBody] AttemptContextInput input, CancellationToken ct)
        => Ok(new { recorded = await anomalies.RecordContextAsync(attemptId, input, ct) });

    [HttpPost("exams/{examId:guid}/detect")]
    public async Task<IActionResult> Detect([FromRoute] Guid examId, CancellationToken ct)
        => Ok(new { flagsCreated = await anomalies.DetectAsync(examId, ct) });

    [HttpGet("exams/{examId:guid}/flags")]
    public async Task<IActionResult> Flags([FromRoute] Guid examId, CancellationToken ct)
        => Ok(await sender.Send(new ListAnomalyFlags(examId), ct));

    [HttpPost("flags/{flagId:guid}/review")]
    public async Task<IActionResult> Review([FromRoute] Guid flagId, [FromQuery] bool reviewed, CancellationToken ct)
        => Ok(new { reviewed = await sender.Send(new ReviewFlag(flagId, reviewed), ct) });

    [HttpGet("exams/{examId:guid}/events")]
    public async Task<IActionResult> Events([FromRoute] Guid examId, CancellationToken ct)
    {
        var events = await sender.Send(new ProctorHistory(examId), ct);
        return Ok(events);
    }
}

public sealed record ProctorHistory(Guid ExamId) : MediatR.IRequest<IReadOnlyList<ProctorHistoryItem>>;

public sealed record ProctorHistoryItem(Guid AttemptId, string CandidateName, string Type,
    string Severity, DateTimeOffset At, string? Detail);

public sealed class ProctorHistoryHandler(Infrastructure.Persistence.AppDbContext db)
    : MediatR.IRequestHandler<ProctorHistory, IReadOnlyList<ProctorHistoryItem>>
{
    public async Task<IReadOnlyList<ProctorHistoryItem>> Handle(ProctorHistory request, CancellationToken ct)
    {
        IQueryable<ProctorHistoryItem> query =
            from e in db.ProctorEvents
            join a in db.Attempts on e.AttemptId equals a.Id
            where a.ExamId == request.ExamId
            orderby e.OccurredAt descending
            select new ProctorHistoryItem(a.Id, a.CandidateName, e.EventType.ToString(),
                e.Severity.ToString(), e.OccurredAt, e.DetailJson);
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query.Take(500), ct);
    }
}
