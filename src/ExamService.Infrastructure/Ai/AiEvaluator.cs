using System.Net.Http.Json;
using ExamService.Application.Common;
using ExamService.Domain.Common;
using Microsoft.Extensions.Configuration;

namespace ExamService.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible evaluator when AI__Endpoint is configured; otherwise a deterministic
/// local heuristic that still produces per-candidate reports and integrity scores.
/// </summary>
public sealed class AiEvaluator(IConfiguration configuration, HttpClient http) : IAiEvaluator
{
    private readonly string? _endpoint = configuration["AI:Endpoint"];
    private readonly string? _key = configuration["AI:ApiKey"];
    private readonly string? _model = configuration["AI:Model"];

    public async Task<AiAnswerEvaluation> EvaluateAnswerAsync(
        string question, string answer, decimal maxMarks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_endpoint) || string.IsNullOrWhiteSpace(answer))
            return LocalEval(answer, maxMarks);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (!string.IsNullOrWhiteSpace(_key)) request.Headers.Add("Authorization", $"Bearer {_key}");
            request.Content = JsonContent.Create(new
            {
                model = _model ?? "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "You are a strict exam grader. Respond with JSON: {\"marks\":number,\"feedback\":string,\"needsHumanReview\":boolean}. Marks must be between 0 and the maximum." },
                    new { role = "user", content = $"Question: {question}\nMaximum marks: {maxMarks}\nStudent answer: {answer}" }
                },
                temperature = 0
            });
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var clean = content.Contains('{') ? content[content.IndexOf('{')..] : content;
            using var parsed = System.Text.Json.JsonDocument.Parse(clean);
            var marks = Math.Clamp(parsed.RootElement.GetProperty("marks").GetDecimal(), 0m, maxMarks);
            return new AiAnswerEvaluation(marks,
                parsed.RootElement.GetProperty("feedback").GetString() ?? "Reviewed.",
                parsed.RootElement.TryGetProperty("needsHumanReview", out var n) && n.GetBoolean());
        }
        catch (Exception)
        {
            return LocalEval(answer, maxMarks);
        }
    }

    public async Task<AiIntegrityAssessment> AssessIntegrityAsync(
        IReadOnlyList<ProctorEventView> events, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_endpoint))
            return LocalIntegrity(events);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (!string.IsNullOrWhiteSpace(_key)) request.Headers.Add("Authorization", $"Bearer {_key}");
            request.Content = JsonContent.Create(new
            {
                model = _model ?? "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = "Analyze proctoring events and respond with JSON: {\"riskScore\":number 0-100,\"summary\":string,\"recommendation\":string,\"shouldFlag\":boolean}." },
                    new { role = "user", content = string.Join("\n", events.Select(e => $"{e.At:O} {e.Type} {e.Severity} {e.Detail}")) }
                },
                temperature = 0
            });
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var clean = content.Contains('{') ? content[content.IndexOf('{')..] : content;
            using var parsed = System.Text.Json.JsonDocument.Parse(clean);
            return new AiIntegrityAssessment(
                Math.Clamp(parsed.RootElement.GetProperty("riskScore").GetDecimal(), 0m, 100m),
                parsed.RootElement.GetProperty("summary").GetString() ?? "",
                parsed.RootElement.GetProperty("recommendation").GetString() ?? "",
                parsed.RootElement.GetProperty("shouldFlag").GetBoolean());
        }
        catch (Exception)
        {
            return LocalIntegrity(events);
        }
    }

    private static AiAnswerEvaluation LocalEval(string answer, decimal maxMarks)
    {
        var length = answer.Trim().Length;
        var marks = length == 0 ? 0m : Math.Min(maxMarks, Math.Max(maxMarks / 3m, maxMarks * Math.Min(1m, length / 200m)));
        return new AiAnswerEvaluation(decimal.Round(marks, 2),
            "Local heuristic score; human review recommended for subjective accuracy.", true);
    }

    private static AiIntegrityAssessment LocalIntegrity(IReadOnlyList<ProctorEventView> events)
    {
        var weights = new Dictionary<EventType, decimal>
        {
            [EventType.TabBlur] = 8, [EventType.Copy] = 12, [EventType.Paste] = 15,
            [EventType.FullscreenExit] = 20, [EventType.CameraBlocked] = 35,
            [EventType.MicBlocked] = 30, [EventType.FaceAbsent] = 12,
            [EventType.MultipleFaces] = 30, [EventType.FaceCovered] = 35,
            [EventType.AudioAnomaly] = 18, [EventType.Disconnected] = 6,
            [EventType.ForcedSubmit] = 25
        };
        var score = Math.Min(100m, events.Sum(e => weights.GetValueOrDefault(e.Type, 5)));
        return new AiIntegrityAssessment(score,
            $"Analyzed {events.Count} proctoring events; {events.Count(e => e.Severity >= Severity.High)} high-severity.",
            score >= 50 || events.Any(e => e.Severity >= Severity.High)
                ? "Flag for human review." : "No action required.",
            score >= 50 || events.Any(e => e.Severity >= Severity.High));
    }
}
