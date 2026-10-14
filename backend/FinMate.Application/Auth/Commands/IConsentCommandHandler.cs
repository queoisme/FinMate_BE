using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface IConsentCommandHandler
{
    Task<UserProfileDto> AcceptAsync(AcceptConsentCommand command, CancellationToken ct = default);

    /// <summary>Rút lại đồng ý AI hoặc đồng ý đóng góp dữ liệu huấn luyện. Điều khoản chung không rút được (muốn thôi thì xoá tài khoản).</summary>
    Task<UserProfileDto> WithdrawAsync(Guid userId, ConsentKind kind, CancellationToken ct = default);
}
