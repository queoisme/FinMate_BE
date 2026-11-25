using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.Security;

public sealed class SecretCipherExportArchiveProtector : IExportArchiveProtector
{
    private readonly SecretCipher _cipher;

    public SecretCipherExportArchiveProtector(SecretCipher cipher)
    {
        _cipher = cipher;
    }

    public bool Available => _cipher.Available;

    public (byte[] Ciphertext, byte[] Nonce) Protect(Guid exportId, byte[] plaintext)
        => _cipher.EncryptBytes(Context(exportId), plaintext);

    public byte[]? Unprotect(Guid exportId, byte[] ciphertext, byte[] nonce)
        => _cipher.DecryptBytes(Context(exportId), ciphertext, nonce);

    private static string Context(Guid exportId) => $"data-export:{exportId:N}";
}
