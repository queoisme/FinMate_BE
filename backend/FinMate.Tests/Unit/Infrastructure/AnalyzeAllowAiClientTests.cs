using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Cờ đồng ý AI của /analyze đi sang ai-service dưới tên <c>allow_ai</c>; mặc định false.</summary>
public class AnalyzeAllowAiClientTests
{
    private static async Task<AnalyzeApiRequest> SendAsync(AnalyzeRequest request)
    {
        var api = new Mock<IAIServiceApi>();
        AnalyzeApiRequest? sent = null;
        api.Setup(a => a.AnalyzeAsync(It.IsAny<AnalyzeApiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyzeApiRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new AnalyzeApiResponse(
                "non_financial", new ClassifierApiResult("non_financial", 0.9), null, null, null, null, 10));
        await new AIServiceClient(api.Object).AnalyzeAsync(request);
        return sent!;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllowAiIsSentAsSnakeCase(bool allowAi)
    {
        var sent = await SendAsync(new AnalyzeRequest(Guid.NewGuid(), Guid.NewGuid(), "com.mbmobile", null, "body", DateTimeOffset.UtcNow, allowAi));

        sent.AllowAi.Should().Be(allowAi);
        JsonSerializer.Serialize(sent, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().Contain($"\"allow_ai\":{allowAi.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public async Task AllowAiDefaultsToFalseWhenRequestOmitsIt()
    {
        var sent = await SendAsync(new AnalyzeRequest(Guid.NewGuid(), Guid.NewGuid(), "com.mbmobile", null, "body", DateTimeOffset.UtcNow));

        sent.AllowAi.Should().BeFalse();
    }
}
