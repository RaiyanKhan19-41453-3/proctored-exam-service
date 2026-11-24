using ExamService.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ExamService.Tests.Unit;

public class AnalyzerTests
{
    [Fact]
    public async Task WithoutVisionEndpoint_DoesNotFabricateMissingFace()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AI:Endpoint"] = "" }).Build();
        var analyzer = new EvidenceAnalyzer(config, new HttpClient());
        var result = await analyzer.AnalyzeFrameAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
        Assert.True(result.FacePresent);
        Assert.Equal(1, result.FaceCount);
        Assert.False(result.FaceCovered);
    }
}
