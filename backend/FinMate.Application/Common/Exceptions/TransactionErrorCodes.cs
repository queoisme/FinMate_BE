namespace FinMate.Application.Common.Exceptions;

public static class TransactionErrorCodes
{
    public const string NotDraft = "TRANSACTION_NOT_DRAFT";

    /// <summary>Ví nguồn và ví đích của transfer trùng nhau.</summary>
    public const string TransferSameAccount = "TRANSACTION_TRANSFER_SAME_ACCOUNT";

    /// <summary>Tạo transfer qua endpoint giao dịch thường thay vì <c>POST /transactions/transfer</c>.</summary>
    public const string UseTransferEndpoint = "TRANSACTION_USE_TRANSFER_ENDPOINT";

    /// <summary>Đổi qua lại giữa transfer và debit/credit khi sửa giao dịch.</summary>
    public const string TypeChangeNotAllowed = "TRANSACTION_TYPE_CHANGE_NOT_ALLOWED";

    /// <summary>Ảnh hóa đơn vượt giới hạn kích thước.</summary>
    public const string ReceiptImageTooLarge = "TRANSACTION_RECEIPT_IMAGE_TOO_LARGE";

    /// <summary>Ảnh hóa đơn rỗng hoặc không phải định dạng ảnh được chấp nhận.</summary>
    public const string ReceiptImageInvalid = "TRANSACTION_RECEIPT_IMAGE_INVALID";

    /// <summary>Audio rỗng, quá 10 MiB hoặc không phải định dạng âm thanh được chấp nhận.</summary>
    public const string VoiceAudioInvalid = "TRANSACTION_VOICE_AUDIO_INVALID";

    /// <summary>Không nghe ra lời nào (im lặng hoặc quá nhiễu): người dùng nên ghi lại hoặc gõ tay.</summary>
    public const string VoiceNoSpeech = "TRANSACTION_VOICE_NO_SPEECH";

    // ---- Hoàn tiền / đảo giao dịch (Phase 81). Mã giữ đúng tên đã chốt trong đề bài, không thêm tiền tố TRANSACTION_. ----

    /// <summary>422: tổng các khoản hoàn đã xác nhận sẽ vượt số tiền khoản gốc (BR-RF-02).</summary>
    public const string RefundExceedsOriginal = "REFUND_EXCEEDS_ORIGINAL";

    /// <summary>409: xoá, đổi số tiền hoặc đổi loại một khoản gốc còn khoản hoàn/đảo đã xác nhận (BR-RF-11).</summary>
    public const string HasRefunds = "HAS_REFUNDS";

    /// <summary>422: khoản gốc không nhận hoàn được: không phải chi đã xác nhận, là cho vay/mượn, hoặc đã bị đảo (D12).</summary>
    public const string RefundOriginalNotEligible = "REFUND_ORIGINAL_NOT_ELIGIBLE";

    /// <summary>422: lệnh đảo phải bằng đúng số tiền gốc và khoản gốc chưa được hoàn phần nào (D4).</summary>
    public const string ReversalAmountMismatch = "REVERSAL_AMOUNT_MISMATCH";

    /// <summary>422: danh mục của khoản hoàn đã liên kết đi theo khoản gốc, không sửa riêng được (D13).</summary>
    public const string RefundCategoryLocked = "REFUND_CATEGORY_LOCKED";

    /// <summary>422: giao dịch này không mang được quan hệ hoàn/đảo (không phải tiền vào, hoặc trỏ vào chính nó).</summary>
    public const string RefundNotLinkable = "REFUND_NOT_LINKABLE";

    /// <summary>422: khoản hoàn đi theo khoản gốc vào/ra nhóm, không chia sẻ hay bỏ chia sẻ riêng được (BR-RF-10).</summary>
    public const string RefundFollowsOriginal = "REFUND_FOLLOWS_ORIGINAL";

    // ---- Bản trùng có liên kết (Phase 82) ----

    /// <summary>422: hai giao dịch không gộp được (khác chiều/số tiền, có khoản hoàn, đã chia sẻ nhóm, cả hai đã khớp QR, đã gộp vào khoản khác…); lý do nằm trong message.</summary>
    public const string MergeNotAllowed = "MERGE_NOT_ALLOWED";

    /// <summary>422: "Không trùng" cho một giao dịch không có liên kết nghi trùng nào.</summary>
    public const string NotSuspectedDuplicate = "NOT_SUSPECTED_DUPLICATE";
}
