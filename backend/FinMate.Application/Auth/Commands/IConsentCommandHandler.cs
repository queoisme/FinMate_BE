using FinMate.Application.Common.Models;

namespace FinMate.Application.Auth.Commands;

public interface IConsentCommandHandler
{
    Task<UserProfileDto> AcceptAsync(AcceptConsentCommand command, CancellationToken ct = default);

    /// <summary>Rút lại đồng ý gửi tên món cho AI. Điều khoản chung không rút được (muốn thôi thì xoá tài khoản).</summary>
    Task<UserProfileDto> WithdrawAiAsync(Guid userId, CancellationToken ct = default);
}
