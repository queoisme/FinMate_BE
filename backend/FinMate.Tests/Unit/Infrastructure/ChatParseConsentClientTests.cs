using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Cờ đồng ý AI đi sang ai-service dưới tên <c>allow_ai</c>; mã người dùng vẫn chỉ là mã băm.</summary>
public class ChatParseConsentClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllowAiIsSentAsSnakeCaseAndTheUserOnlyAsAHash(bool allowAi)
    {
        var api = new Mock<IAIServiceApi>();
        ChatParseApiRequest? sent = null;
        api.Setup(a => a.ChatParseAsync(It.IsAny<ChatParseApiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatParseApiRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new ChatParseApiResponse("out_of_scope", null, null, null, null));
        var userId = Guid.NewGuid();

        await new AIServiceClient(api.Object).ParseChatAsync(new ChatParseRequest(userId, "mua ổ cắm điện 60k", allowAi));

        sent!.AllowAi.Should().Be(allowAi);
        sent.UserIdHash.Should().MatchRegex("^[0-9a-f]{64}$");
        JsonSerializer.Serialize(sent, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().Contain($"\"allow_ai\":{allowAi.ToString().ToLowerInvariant()}");
    }
}
