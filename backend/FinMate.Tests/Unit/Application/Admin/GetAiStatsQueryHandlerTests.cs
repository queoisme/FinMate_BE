using FinMate.Application.Admin.Queries;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

public class GetAiStatsQueryHandlerTests
{
    private readonly Mock<IAiStatsRepository> _aiStatsRepository = new();
    private readonly Mock<IAIServiceClient> _aiServiceClient = new();
    private readonly GetAiStatsQueryHandler _handler;

    public GetAiStatsQueryHandlerTests()
    {
        _handler = new GetAiStatsQueryHandler(
            _aiStatsRepository.Object,
            _aiServiceClient.Object,
            NullLogger<GetAiStatsQueryHandler>.Instance);

        _aiStatsRepository
            .Setup(r => r.GetProductionStatsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(categorised: 10, corrections: 3, draftsCreated: 20, draftsConfirmed: 15));
    }

    [Fact]
    public async Task WhenTheAiServiceIsDownTheBackendHalfStillComesBack()
    {
        // Nửa quan trọng hơn của màn hình này nằm ngay trong backend DB. Để exception thoát ra
        // sẽ biến "AI Service đang restart" thành "màn hình quản trị sập".
        _aiServiceClient
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("AI Service không phản hồi."));

        var stats = await _handler.HandleAsync(new GetAiStatsQuery(null, null));

        stats.AiService.Should().BeNull();
        stats.AiServiceError.Should().NotBeNullOrEmpty();
        stats.Production.TotalAnalyzed.Should().Be(100);
        stats.Production.CategoryCorrectionRate.Should().Be(0.3);
    }

    [Fact]
    public async Task RatesAreNullWhenThereIsNothingToDivideBy()
    {
        // "Chưa có dữ liệu" khác hẳn "AI đoán đúng 100%". Ép về 0 làm một hệ thống trống rỗng
        // trông như một hệ thống hoàn hảo.
        _aiStatsRepository
            .Setup(r => r.GetProductionStatsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(categorised: 0, corrections: 0, draftsCreated: 0, draftsConfirmed: 0));
        _aiServiceClient
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        var stats = await _handler.HandleAsync(new GetAiStatsQuery(null, null));

        stats.Production.CategoryCorrectionRate.Should().BeNull();
        stats.Production.DraftConfirmRate.Should().BeNull();
    }

    [Fact]
    public async Task DefaultWindowIsTheLastThirtyDays()
    {
        _aiServiceClient
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        var stats = await _handler.HandleAsync(new GetAiStatsQuery(null, null));

        (stats.Production.To - stats.Production.From).Should().BeCloseTo(TimeSpan.FromDays(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BothHalvesAreReturnedWhenTheAiServiceAnswers()
    {
        _aiServiceClient
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiServiceStats(
                new[] { new AiModelStatus("classifier", "1.0.0", DateTimeOffset.UtcNow, 0.98, 0.97, "test") },
                RawSampleCount: 236,
                LabeledSampleCount: 236,
                UnlabeledSampleCount: 0,
                SplitCounts: new Dictionary<string, int> { ["train"] = 168 },
                LastTrainingJob: null,
                PendingFeedbackCount: 4));

        var stats = await _handler.HandleAsync(new GetAiStatsQuery(null, null));

        stats.AiServiceError.Should().BeNull();
        stats.AiService!.Models.Should().ContainSingle(m => m.Stage == "classifier" && m.Version == "1.0.0");
        stats.AiService.PendingFeedbackCount.Should().Be(4);
    }

    private static AiProductionStats Stats(
        int categorised, int corrections, int draftsCreated, int draftsConfirmed) => new(
        TotalAnalyzed: 100,
        ByPipelineResult: new Dictionary<string, int> { ["Financial"] = 80, ["NonFinancial"] = 20 },
        ByPackageName: new Dictionary<string, int> { ["com.mbmobile"] = 100 },
        ByClassifierVersion: new Dictionary<string, int> { ["1.0.0"] = 100 },
        PotentialDuplicates: 2,
        DraftsCreated: draftsCreated,
        DraftsConfirmed: draftsConfirmed,
        CategoryCorrections: corrections,
        CategorisedTransactions: categorised,
        AvgClassifierConfidence: 0.9,
        AvgExtractionConfidence: 0.95,
        AvgCategorizationConfidence: 0.8,
        AvgProcessingMs: 12.5,
        MaxProcessingMs: 640);
}
