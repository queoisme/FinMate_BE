using System.Security.Cryptography;
using System.Text;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// AGENTS.md §3.1: AI DB không lưu user_id/transaction_id thực — luôn gửi SHA-256 (hex thường) qua HTTP.
/// Dùng chung cho AI Service và OCR service để cùng một người dùng ra cùng một mã ở cả hai nơi.
/// </summary>
internal static class UserIdHasher
{
    public static string Hash(Guid value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToString());
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
