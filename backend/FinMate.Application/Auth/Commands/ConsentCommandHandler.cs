using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;

namespace FinMate.Application.Auth.Commands;

public class ConsentCommandHandler : IConsentCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;
    private readonly ITrainingSampleService? _trainingSamples;  // null: không có dịch vụ thu mẫu thì không có gì để xóa

    public ConsentCommandHandler(IUserRepository userRepository, ICacheService cacheService, ITrainingSampleService? trainingSamples = null)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
        _trainingSamples = trainingSamples;
    }

    public async Task<UserProfileDto> AcceptAsync(AcceptConsentCommand command, CancellationToken ct = default)
    {
        var current = command.Kind switch
        {
            ConsentKind.Terms => ConsentVersions.Terms,
            ConsentKind.Ai => ConsentVersions.Ai,
            ConsentKind.Training => ConsentVersions.Training,
            _ => throw UnknownKind(),
        };
        if (command.Version != current)
        {
            throw new ConflictException(AuthErrorCodes.ConsentVersionOutdated,
                "Nội dung chính sách đã thay đổi. Hãy cập nhật ứng dụng để xem và đồng ý bản mới.");
        }

        var user = await LoadAsync(command.UserId, ct);
        var now = DateTimeOffset.UtcNow;
        switch (command.Kind)
        {
            case ConsentKind.Terms:
                user.TermsVersion = current;
                user.TermsAcceptedAt = now;
                // Điều khoản đã nêu việc gửi tên món cho AI nên một lần đồng ý là đủ; trừ người đã chủ động tắt AI thì giữ nguyên lựa chọn của họ.
                if (user.AiConsentVersion != ConsentVersions.AiOptedOut)
                {
                    user.AiConsentVersion = ConsentVersions.Ai;
                    user.AiConsentAt = now;
                }

                break;
            case ConsentKind.Ai:
                user.AiConsentVersion = current;
                user.AiConsentAt = now;
                break;
            default:
                user.TrainingConsentVersion = current;
                user.TrainingConsentAt = now;
                break;
        }

        return await SaveAsync(user, ct);
    }

    public async Task<UserProfileDto> WithdrawAsync(Guid userId, ConsentKind kind, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind))
        {
            throw UnknownKind();
        }

        if (kind == ConsentKind.Terms)
        {
            throw new BusinessRuleException(AuthErrorCodes.ConsentNotWithdrawable, "Điều khoản chung không rút lại được. Muốn ngừng sử dụng, hãy xoá tài khoản.");
        }

        var user = await LoadAsync(userId, ct);
        if (kind == ConsentKind.Ai)
        {
            user.AiConsentVersion = ConsentVersions.AiOptedOut;  // ghi nhận là chủ động tắt, không phải "chưa quyết định"
            user.AiConsentAt = null;
        }
        else
        {
            user.TrainingConsentVersion = null;
            user.TrainingConsentAt = null;
        }

        var profile = await SaveAsync(user, ct);
        if (kind == ConsentKind.Training && _trainingSamples is not null)
        {
            // Rút đồng ý thì dữ liệu đã đóng góp cũng phải bị xóa. Việc rút đã lưu (không thu thêm nữa); nếu chưa xóa được thì báo lỗi để người dùng bấm lại, không báo "đã xóa" khi chưa.
            if (!await _trainingSamples.EraseAsync(userId, ct))
            {
                throw new AIServiceUnavailableException("Đã dừng thu thêm dữ liệu, nhưng chưa xóa được dữ liệu bạn đã đóng góp. Thử lại sau ít phút nhé.");
            }
        }

        return profile;
    }

    private static BusinessRuleException UnknownKind() =>
        new(AuthErrorCodes.ConsentKindUnknown, "Loại đồng ý không hợp lệ.");

    private async Task<User> LoadAsync(Guid userId, CancellationToken ct) =>
        await _userRepository.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

    private async Task<UserProfileDto> SaveAsync(User user, CancellationToken ct)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);
        await _cacheService.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);
        return UserProfileDto.From(user);
    }
}
