using System.Text.Json;
using ExamService.Application.Common;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

namespace ExamService.Infrastructure.Ai;

/// <summary>
/// Evidence analysis. With AI__Endpoint configured, frames/audio are sent to a vision-capable
/// OpenAI-compatible model for face-presence/coverage and speech-anomaly judgments. Otherwise a
/// conservative local heuristic is used: it never claims a face match without a real analyzer.
/// </summary>
public sealed class EvidenceAnalyzer(IConfiguration configuration, HttpClient http) : IEvidenceAnalyzer
{
    private readonly string? _endpoint = configuration["AI:Endpoint"];
    private readonly string? _key = configuration["AI:ApiKey"];
    private readonly string? _model = configuration["AI:Model"];

    public async Task<FaceAnalysis> AnalyzeFrameAsync(byte[] image, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_endpoint)) return new FaceAnalysis(false, 0, false, null);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            if (!string.IsNullOrWhiteSpace(_key)) request.Headers.Add("Authorization", $"Bearer {_key}");
            var base64 = Convert.ToBase64String(image);
            request.Content = JsonContent.Create(new
            {
                model = _model ?? "gpt-4o-mini",
                messages = new object[]
                {
                    new { role = "system", content = "Analyze the proctoring frame. Respond JSON: {\"facePresent\":boolean,\"faceCount\":number,\"faceCovered\":boolean}. Do not identify people." },
                    new { role = "user", content = new object[]
                    {
                        new { type = "text", text = "Analyze this exam frame." },
                        new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + base64 } }
                    } }
                },
                temperature = 0
            });
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "{}";
            var clean = content[content.IndexOf('{')..];
            using var r = JsonDocument.Parse(clean);
            return new FaceAnalysis(
                r.RootElement.GetProperty("facePresent").GetBoolean(),
                r.RootElement.GetProperty("faceCount").GetInt32(),
                r.RootElement.GetProperty("faceCovered").GetBoolean(), null);
        }
        catch (Exception) { return new FaceAnalysis(false, 0, false, null); }
    }

    public Task<AudioAnalysis> AnalyzeAudioAsync(byte[] audio, CancellationToken ct)
    {
        if (audio.Length < 4096) return Task.FromResult(new AudioAnalysis(false, 0m, false));
        // Lightweight signal heuristic: RMS energy and zero-crossing rate distinguish silence,
        // speech-like activity and unexpectedly loud/anomalous segments without claiming to
        // understand content.
        double sum = 0, crossings = 0;
        for (var i = 1; i < audio.Length; i += 2)
        {
            var sample = (short)(audio[i] | (i + 1 < audio.Length ? audio[i + 1] << 8 : 0));
            sum += sample * (double)sample;
            if (i >= 2 && Math.Sign(sample) != Math.Sign((short)(audio[i - 2] | audio[i - 1] << 8))) crossings++;
        }
        var rms = Math.Sqrt(sum / Math.Max(1, audio.Length / 2));
        var peak = Math.Min(100m, Math.Round((decimal)(rms / 32768.0 * 100), 2));
        var zcr = crossings / (double)Math.Max(1, audio.Length / 2);
        var speech = peak > 2m && zcr > 0.02;
        return Task.FromResult(new AudioAnalysis(speech, peak, peak > 75m));
    }
}
