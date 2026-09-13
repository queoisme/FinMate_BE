using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Ảnh hóa đơn → các trường để client điền sẵn form (docx phương thức 3).
///
/// Không persist gì, đúng như <see cref="ParseNaturalLanguageCommandHandler"/>: docx yêu cầu
/// người dùng rà soát rồi mới lưu, nên giao dịch chỉ ra đời khi họ bấm Lưu và đi qua
/// <c>POST /transactions</c> với <c>source = Receipt</c>.
///
/// Ảnh cũng không được lưu ở đâu: nó chỉ đi qua bộ nhớ tiến trình rồi sang AI Service.
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

    private readonly IAIServiceClient _aiServiceClient;

    public ScanReceiptCommandHandler(IAIServiceClient aiServiceClient)
    {
        _aiServiceClient = aiServiceClient;
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

        var response = await _aiServiceClient.ScanReceiptAsync(
            new ScanReceiptRequest(command.UserId, command.Image, command.FileName, command.ContentType),
            ct);

        return new ScannedReceiptDto(
            response.OcrResult,
            response.Extraction?.AmountCents,
            response.Extraction?.TransactionType,
            response.Extraction?.MerchantName,
            response.Extraction?.TransactedAt,
            response.Categorization?.CategorySlug,
            response.Extraction?.Confidence);
    }
}
