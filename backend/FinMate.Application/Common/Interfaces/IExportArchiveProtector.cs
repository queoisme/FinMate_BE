namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Mã hoá gói dữ liệu cá nhân khi lưu (Phase 91, BR-EXP-09). Cùng cơ chế với ảnh ticket (<see cref="IAttachmentProtector"/>) nhưng ngữ cảnh riêng,
/// gắn với id yêu cầu: chép bản mã sang yêu cầu khác (hay sang ảnh ticket) thì không giải mã được.
/// </summary>
public interface IExportArchiveProtector
{
    /// <summary>False khi thiếu <c>SECRETS_MASTER_KEY</c>: không được lưu gói (D8).</summary>
    bool Available { get; }

    (byte[] Ciphertext, byte[] Nonce) Protect(Guid exportId, byte[] plaintext);

    /// <summary>Null nếu sai khóa hoặc dữ liệu đã bị sửa.</summary>
    byte[]? Unprotect(Guid exportId, byte[] ciphertext, byte[] nonce);
}
