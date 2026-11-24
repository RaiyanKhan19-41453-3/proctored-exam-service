using ExamService.Application.DTOs;
using ExamService.Application.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamService.Api;

[ApiController]
[Route("api/exams")]
[Authorize]
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
public sealed class AttemptsController(IAttemptService attempts) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start([FromQuery] Guid examId,
        [FromBody] StartAttemptRequest request, CancellationToken ct)
        => Ok(new { attemptId = await attempts.StartAsync(examId, request, ct) });

    [HttpPost("{id:guid}/answers")]
    public async Task<IActionResult> SaveAnswer([FromRoute] Guid id,
        [FromBody] SaveAnswerRequest request, CancellationToken ct)
        => Ok(new { saved = await attempts.SaveAnswerAsync(id, request, ct) });

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
[Authorize]
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
