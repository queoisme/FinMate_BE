using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Dùng khi chưa cấu hình `GOOGLE_CLIENT_SECRET`. Tồn tại để container DI vẫn dựng được
/// controller, và lỗi rơi đúng vào chỗ kiểm cấu hình nói rõ nguyên nhân — thay vì một lỗi
/// "không resolve được service" chẳng chỉ ra điều gì.
/// </summary>
public class DisabledGoogleCodeExchanger : IGoogleCodeExchanger
{
    public Task<string?> ExchangeForIdTokenAsync(string code, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
