namespace ExamService.Application.Common;

public interface IEvidenceAnalyzer
{
    Task<FaceAnalysis> AnalyzeFrameAsync(byte[] image, CancellationToken ct = default);
    Task<AudioAnalysis> AnalyzeAudioAsync(byte[] audio, CancellationToken ct = default);
}

public sealed record FaceAnalysis(bool FacePresent, int FaceCount, bool FaceCovered, decimal? MatchScore);
public sealed record AudioAnalysis(bool SpeechDetected, decimal PeakLevel, bool Anomaly);
