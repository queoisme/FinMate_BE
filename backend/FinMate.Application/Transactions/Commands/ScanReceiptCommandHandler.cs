using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Ảnh hóa đơn → các trường để client điền sẵn form (docx phương thức 3).
///
/// Không persist gì, đúng như <see cref="ParseNaturalLanguageCommandHandler"/>: docx yêu cầu
/// người dùng rà soát rồi mới lưu, nên giao dịch chỉ ra đời khi họ bấm Lưu và đi qua
/// <c>POST /transactions</c> với <c>source = Receipt</c>.
///
/// Ảnh cũng không được lưu ở đâu: nó chỉ đi qua bộ nhớ tiến trình rồi sang AI Service / OCR service.
///
/// Có <c>OCR_SERVICE_URL</c> thì backend điều phối: OCR service đọc và lọc (ra tổng tiền, tên, ngày, chữ đã lọc), rồi AI Service
/// phân loại chữ đã lọc đó (<c>POST /api/v1/bill/classify</c>). Không cấu hình thì quay về <c>POST /api/v1/ocr</c> như trước.
/// </summary>
public class ScanReceiptCommandHandler : IScanReceiptCommandHandler
{
    // Khớp giới hạn phía AI Service. Chặn ở cả hai đầu: chặn sớm ở đây để một ảnh 50MB
    // không phải đi hết một vòng mạng nội bộ mới bị từ chối.
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif",
    };

    private const string SuccessResult = "success";
    private const string NoAmountResult = "no_amount";
    private const string UnreadableResult = "unreadable";
    private const string DebitType = "debit";
    private const string LabelledTotalSource = "text";

    private readonly IAIServiceClient _aiServiceClient;
    private const int MaxMerchantLength = 200;

    private readonly IOcrServiceClient _ocrServiceClient;
    private readonly IReceiptScanRepository _receiptScanRepository;

    public ScanReceiptCommandHandler(
        IAIServiceClient aiServiceClient, IOcrServiceClient ocrServiceClient, IReceiptScanRepository receiptScanRepository)
    {
        _aiServiceClient = aiServiceClient;
        _ocrServiceClient = ocrServiceClient;
        _receiptScanRepository = receiptScanRepository;
    }

    public async Task<ScannedReceiptDto> HandleAsync(
        ScanReceiptCommand command, CancellationToken ct = default)
    {
        if (command.Image.Length == 0)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageInvalid, "Ảnh rỗng.");
        }

        if (command.Image.Length > MaxImageBytes)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageTooLarge,
                $"Ảnh tối đa {MaxImageBytes / (1024 * 1024)}MB.");
        }

        if (!AllowedContentTypes.Contains(command.ContentType))
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageInvalid,
                "Chỉ nhận ảnh JPEG, PNG, WebP hoặc HEIC.");
        }

        if (_ocrServiceClient.IsConfigured)
        {
            return await ScanViaOcrServiceAsync(command, ct);
        }

        var response = await _aiServiceClient.ScanReceiptAsync(
            new ScanReceiptRequest(command.UserId, command.Image, command.FileName, command.ContentType),
            ct);

        var dto = new ScannedReceiptDto(
            response.OcrResult,
            response.Extraction?.AmountCents,
            response.Extraction?.TransactionType,
            response.Extraction?.MerchantName,
            response.Extraction?.TransactedAt,
            response.Categorization?.CategorySlug,
            response.Extraction?.Confidence);
        return await RecordAsync(command.UserId, dto, categorySource: null, needsReview: false, ct);
    }

    private async Task<ScannedReceiptDto> ScanViaOcrServiceAsync(ScanReceiptCommand command, CancellationToken ct)
    {
        var ocr = await _ocrServiceClient.AnalyzeAsync(
            new OcrServiceRequest(command.UserId, command.Image, command.FileName, command.ContentType), ct);

        if (!ocr.Readable)
        {
            return await RecordAsync(command.UserId, Empty(UnreadableResult), null, false, ct);
        }

        if (ocr.TotalAmount is not > 0)
        {
            return await RecordAsync(command.UserId, Empty(NoAmountResult), null, ocr.NeedsReview, ct);
        }

        var merchant = ocr.MerchantDisplay ?? ocr.MerchantName;
        var category = await ClassifyAsync(command.UserId, ocr, ct);
        var confidence = ReceiptConfidence.Compute(
            hasAmount: true,
            amountFromLabel: ocr.TotalSource == LabelledTotalSource,
            hasMerchant: !string.IsNullOrWhiteSpace(merchant),
            hasDate: ocr.TransactedAt is not null,
            needsReview: ocr.NeedsReview);

        var dto = new ScannedReceiptDto(
            SuccessResult, ocr.TotalAmount, DebitType, merchant, ocr.TransactedAt, category?.CategorySlug, confidence);
        return await RecordAsync(command.UserId, dto, category?.Source, ocr.NeedsReview, ct);
    }

    /// <summary>
    /// Ghi lại kết quả BÓC TÁCH của lần quét (không ảnh, không chữ OCR) và trả về chính nó kèm mã bản ghi. Bản ghi không động tới số dư,
    /// ngân sách hay gamification: lần quét chưa phải giao dịch, giao dịch chỉ ra đời khi người dùng bấm Lưu.
    /// </summary>
    private async Task<ScannedReceiptDto> RecordAsync(
        Guid userId, ScannedReceiptDto dto, string? categorySource, bool needsReview, CancellationToken ct)
    {
        var scan = new ReceiptScan
        {
            UserId = userId,
            OcrResult = dto.OcrResult,
            AmountCents = dto.AmountCents,
            MerchantName = dto.MerchantName is { Length: > MaxMerchantLength } name ? name[..MaxMerchantLength] : dto.MerchantName,
            TransactedAt = dto.TransactedAt,
            CategorySlug = dto.CategorySlug,
            CategorySource = categorySource,
            Confidence = dto.Confidence,
            NeedsReview = needsReview,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _receiptScanRepository.AddAsync(scan, ct);
        return dto with { ScanId = scan.Id };
    }

    /// <summary>
    /// Phân loại là phần phụ: AI Service lỗi thì vẫn trả tổng tiền và tên đọc được, danh mục để trống cho người dùng chọn.
    /// Chặn cả lần quét chỉ vì không phân loại được là bắt người dùng chụp lại một ảnh đã đọc xong.
    /// </summary>
    private async Task<BillClassifyResponse?> ClassifyAsync(Guid userId, OcrServiceResponse ocr, CancellationToken ct)
    {
        // Cửa hàng người dùng đã chốt do AI Service tự tra theo hash người dùng; backend chỉ gửi tên và chữ đã lọc.
        try
        {
            return await _aiServiceClient.ClassifyBillAsync(
                new BillClassifyRequest(userId, ocr.MerchantName ?? ocr.MerchantDisplay, ocr.Lines), ct);
        }
        catch (AIServiceUnavailableException)
        {
            return null;
        }
    }

    private static ScannedReceiptDto Empty(string result)
        => new(result, null, null, null, null, null, null);
}
