using System.Security.Cryptography;
using System.Text;

namespace FinMate.Application.Common;

/// <summary>
/// UUID phiên bản 5 (RFC 4122/9562: SHA-1 trên namespace + tên). Cùng đầu vào luôn ra cùng id, nên dùng làm khoá idempotent cho bản ghi sinh ra
/// từ một bản ghi khác (khoản phí của một Transfer, Phase 83). Không phải bí mật: SHA-1 ở đây chỉ để băm tên, không để bảo mật.
/// </summary>
public static class DeterministicGuid
{
    public static Guid V5(Guid @namespace, string name)
    {
        var namespaceBytes = @namespace.ToByteArray(bigEndian: true);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);

#pragma warning disable CA5350 // SHA-1 là thuật toán RFC quy định cho UUID v5, không dùng để bảo mật.
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);   // phiên bản 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);   // biến thể RFC 4122
        return new Guid(bytes, bigEndian: true);
    }
}
