using System.Diagnostics;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// <c>LearnMerchantAsync</c> chạy ngay sau khi giao dịch đã lưu nên phải là best-effort: AI Service chết hay treo không được làm hỏng hoặc
/// làm chậm việc lưu.
/// </summary>
public class LearnMerchantClientTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly LearnMerchantRequest Request = new(UserId, "Highlands Coffee", "entertainment");

    private readonly Mock<IAIServiceApi> _api = new();

    [Fact]
    public async Task TheAiServiceGetsTheShopTheSlugAndOnlyAHashOfTheUser()
    {
        BillLearnApiRequest? sent = null;
        _api.Setup(a => a.BillLearnAsync(It.IsAny<BillLearnApiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BillLearnApiRequest, CancellationToken>((r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await new AIServiceClient(_api.Object).LearnMerchantAsync(Request);

        sent!.MerchantName.Should().Be("Highlands Coffee");
        sent.CategorySlug.Should().Be("entertainment");
        sent.UserIdHash.Should().MatchRegex("^[0-9a-f]{64}$").And.NotContain(UserId.ToString("N")[..8]);
    }

    [Fact]
    public void TheRequestIsSentWithTheNamesTheAiServiceExpects()
    {
        var json = JsonSerializer.Serialize(
            new BillLearnApiRequest(new string('a', 64), "Highlands Coffee", "entertainment"), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("user_id_hash").GetString().Should().HaveLength(64);
        doc.RootElement.GetProperty("merchant_name").GetString().Should().Be("Highlands Coffee");
        doc.RootElement.GetProperty("category_slug").GetString().Should().Be("entertainment");
    }

    [Fact]
    public async Task ADeadAiServiceNeverThrowsToTheCaller()
    {
        _api.Setup(a => a.BillLearnAsync(It.IsAny<BillLearnApiRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var act = () => new AIServiceClient(_api.Object).LearnMerchantAsync(Request);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AHangingAiServiceIsGivenUpOnAfterAFewSecondsNotWaitedForever()
    {
        _api.Setup(a => a.BillLearnAsync(It.IsAny<BillLearnApiRequest>(), It.IsAny<CancellationToken>()))
            .Returns<BillLearnApiRequest, CancellationToken>((_, ct) => Task.Delay(Timeout.Infinite, ct));
        var clock = Stopwatch.StartNew();

        await new AIServiceClient(_api.Object).LearnMerchantAsync(Request);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2));
    }
}
