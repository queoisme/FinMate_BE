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
}
