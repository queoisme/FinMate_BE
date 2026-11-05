using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.Security;

public sealed class SecretCipherAttachmentProtector : IAttachmentProtector
{
    private readonly SecretCipher _cipher;

    public SecretCipherAttachmentProtector(SecretCipher cipher)
    {
        _cipher = cipher;
    }

    public bool Available => _cipher.Available;

    public (byte[] Ciphertext, byte[] Nonce) Protect(Guid attachmentId, byte[] plaintext)
        => _cipher.EncryptBytes(Context(attachmentId), plaintext);

    public byte[]? Unprotect(Guid attachmentId, byte[] ciphertext, byte[] nonce)
        => _cipher.DecryptBytes(Context(attachmentId), ciphertext, nonce);

    private static string Context(Guid attachmentId) => $"support-attachment:{attachmentId:N}";
}
