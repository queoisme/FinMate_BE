namespace FinMate.Application.Common.Interfaces;

/// <summary>Mã hóa nội dung ảnh trong ticket. Gắn với id của ảnh, nên chép dữ liệu sang dòng khác thì không giải mã được.</summary>
public interface IAttachmentProtector
{
    bool Available { get; }
    (byte[] Ciphertext, byte[] Nonce) Protect(Guid attachmentId, byte[] plaintext);

    /// <summary>Null nếu sai khóa hoặc dữ liệu đã bị sửa.</summary>
    byte[]? Unprotect(Guid attachmentId, byte[] ciphertext, byte[] nonce);
}
