using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Moq;
using Refit;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Gợi ý quan hệ của AI (Phase 81/83) phải tới đúng trường sau khi <c>ProviderReference</c> được thêm vào <see cref="ExtractionResult"/> (2026-11-13).
/// Trước bản sửa này, record chèn <c>ProviderReference</c> vào TRƯỚC <c>RelationHint</c>, nên đường OCR (vẫn truyền theo vị trí cũ) ghi
/// <c>relation_hint</c> vào <c>ProviderReference</c> và <c>transfer_kind</c> vào <c>RelationHint</c>; 8 test hoàn tiền / chuyển ví đỏ vì cùng lý do.
/// </summary>
public class AiClientExtractionMappingTests
{
    private static ExtractionApiResult Extraction()
        => new(2_000_000, "debit", null, "RUT TIEN ATM", DateTimeOffset.UtcNow, 500_000, 0.95, "FT123", "transfer", "atm_withdrawal");

    [Fact]
    public async Task Analyze_KeepsEveryFieldInItsPlace()
    {
        var api = new Mock<IAIServiceApi>();
        api.Setup(a => a.AnalyzeAsync(It.IsAny<AnalyzeApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyzeApiResponse("financial", new ClassifierApiResult("financial", 0.9), Extraction(), null, null, null, 10));

        var result = await new AIServiceClient(api.Object).AnalyzeAsync(
            new AnalyzeRequest(Guid.NewGuid(), Guid.NewGuid(), "com.VCB", null, "body", DateTimeOffset.UtcNow));

        (result.Extraction!.RelationHint, result.Extraction.TransferKind, result.Extraction.ProviderReference)
            .Should().Be(("transfer", "atm_withdrawal", "FT123"));
    }

    [Fact]
    public async Task Ocr_KeepsEveryFieldInItsPlace()
    {
        var api = new Mock<IAIServiceApi>();
        api.Setup(a => a.OcrAsync(It.IsAny<StreamPart>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrApiResponse("success", Extraction(), null, 10));

        var result = await new AIServiceClient(api.Object).ScanReceiptAsync(new ScanReceiptRequest(Guid.NewGuid(), [1, 2, 3], "bill.jpg", "image/jpeg"));

        (result.Extraction!.RelationHint, result.Extraction.TransferKind, result.Extraction.ProviderReference)
            .Should().Be(("transfer", "atm_withdrawal", "FT123"));
    }

    [Fact]
    public void ThePositionalOrderOfThePhase81And83Contract_IsBack()
    {
        // Mọi chỗ đã tạo ExtractionResult theo vị trí trước 2026-11-13 (test, code) phải vẫn đúng.
        var extraction = new ExtractionResult(1, "credit", null, null, null, null, 0.9, "refund", null);

        (extraction.RelationHint, extraction.ProviderReference).Should().Be(("refund", (string?)null));
    }
}
