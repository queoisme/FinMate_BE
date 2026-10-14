using System.Diagnostics;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Gửi mẫu huấn luyện và nhãn cuối chạy ngay sau khi giao dịch đã lưu nên phải là best-effort (như <c>LearnMerchantAsync</c>): AI Service chết hay treo
/// không được làm hỏng hoặc làm chậm việc lưu. Chỉ gửi mã băm của người dùng.
/// </summary>
public class TrainingSampleClientTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ScanId = Guid.NewGuid();
    private static readonly TrainingSampleRequest Sample = new(ScanId, UserId, "Tạp hóa Đức Cường", ["Gao thom 5kg", "Tong cong 125.000"], 125_000, "food", "keyword");
    private static readonly TrainingLabelRequest Label = new(ScanId, UserId, "shopping");

    private readonly Mock<IAIServiceApi> _api = new();

    [Fact]
    public async Task TheSampleCarriesTheScanIdTheFilteredLinesAndOnlyAHashOfTheUser()
    {
        BillSampleApiRequest? sent = null;
        _api.Setup(a => a.BillSampleAsync(It.IsAny<BillSampleApiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BillSampleApiRequest, CancellationToken>((r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await new AIServiceClient(_api.Object).RecordTrainingSampleAsync(Sample);

        sent!.ScanId.Should().Be(ScanId);
        sent.MerchantName.Should().Be("Tạp hóa Đức Cường");
        sent.Lines.Should().Equal("Gao thom 5kg", "Tong cong 125.000");
        sent.TotalAmount.Should().Be(125_000);
        sent.PredictedSlug.Should().Be("food");
        sent.PredictedSource.Should().Be("keyword");
        sent.UserIdHash.Should().MatchRegex("^[0-9a-f]{64}$").And.NotContain(UserId.ToString("N")[..8]);
    }

    [Fact]
    public async Task TheLabelCarriesTheScanIdTheFinalSlugAndOnlyAHashOfTheUser()
    {
        Guid? scanId = null;
        BillSampleLabelApiRequest? sent = null;
        _api.Setup(a => a.BillSampleLabelAsync(It.IsAny<Guid>(), It.IsAny<BillSampleLabelApiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, BillSampleLabelApiRequest, CancellationToken>((id, r, _) => { scanId = id; sent = r; })
            .Returns(Task.CompletedTask);

        await new AIServiceClient(_api.Object).LabelTrainingSampleAsync(Label);

        scanId.Should().Be(ScanId);
        sent!.FinalSlug.Should().Be("shopping");
        sent.UserIdHash.Should().MatchRegex("^[0-9a-f]{64}$").And.NotContain(UserId.ToString("N")[..8]);
    }

    [Fact]
    public void TheBodiesUseTheNamesTheAiServiceExpects()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var sample = JsonDocument.Parse(JsonSerializer.Serialize(
            new BillSampleApiRequest(ScanId, new string('a', 64), "X", ["a"], 1_000, "food", "keyword"), web));
        foreach (var name in new[] { "scan_id", "user_id_hash", "merchant_name", "lines", "total_amount", "predicted_slug", "predicted_source" })
        {
            sample.RootElement.TryGetProperty(name, out _).Should().BeTrue(name);
        }

        using var label = JsonDocument.Parse(JsonSerializer.Serialize(new BillSampleLabelApiRequest(new string('a', 64), "food"), web));
        label.RootElement.GetProperty("user_id_hash").GetString().Should().HaveLength(64);
        label.RootElement.GetProperty("final_slug").GetString().Should().Be("food");
    }

    [Fact]
    public async Task ErasingSendsOnlyAHashOfTheUser()
    {
        BillSampleEraseApiRequest? sent = null;
        _api.Setup(a => a.BillSamplesEraseAsync(It.IsAny<BillSampleEraseApiRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BillSampleEraseApiRequest, CancellationToken>((r, _) => sent = r)
            .Returns(Task.CompletedTask);

        await new AIServiceClient(_api.Object).EraseTrainingSamplesAsync(UserId);

        sent!.UserIdHash.Should().MatchRegex("^[0-9a-f]{64}$").And.NotContain(UserId.ToString("N")[..8]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(sent, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.GetProperty("user_id_hash").GetString().Should().HaveLength(64);
    }

    [Fact]
    public async Task ADeadAiServiceNeverThrowsToTheCaller()
    {
        _api.Setup(a => a.BillSampleAsync(It.IsAny<BillSampleApiRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        _api.Setup(a => a.BillSampleLabelAsync(It.IsAny<Guid>(), It.IsAny<BillSampleLabelApiRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));
        var client = new AIServiceClient(_api.Object);

        await ((Func<Task>)(() => client.RecordTrainingSampleAsync(Sample))).Should().NotThrowAsync();
        await ((Func<Task>)(() => client.LabelTrainingSampleAsync(Label))).Should().NotThrowAsync();
    }

    [Fact]
    public async Task AFailedEraseReachesTheCallerSoItCanBeRetriedAndReported()
    {
        _api.Setup(a => a.BillSamplesEraseAsync(It.IsAny<BillSampleEraseApiRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        await ((Func<Task>)(() => new AIServiceClient(_api.Object).EraseTrainingSamplesAsync(UserId))).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AHangingAiServiceIsGivenUpOnAfterAFewSeconds()
    {
        _api.Setup(a => a.BillSampleAsync(It.IsAny<BillSampleApiRequest>(), It.IsAny<CancellationToken>()))
            .Returns<BillSampleApiRequest, CancellationToken>((_, ct) => Task.Delay(Timeout.Infinite, ct));
        var clock = Stopwatch.StartNew();

        await new AIServiceClient(_api.Object).RecordTrainingSampleAsync(Sample);

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        clock.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2));
    }
}
