using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Legal;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;

namespace FinMate.Application.Auth.Commands;

public class ConsentCommandHandler : IConsentCommandHandler
{
    private readonly IUserRepository _userRepository;
    private readonly ICacheService _cacheService;
    private readonly ILegalVersionProvider _legal;
    private readonly ITrainingSampleService? _trainingSamples;  // null: không có dịch vụ thu mẫu thì không có gì để xóa
    private readonly IAuditLogService? _audit;  // Phase 91 (D3): lịch sử đồng ý cho gói dữ liệu cá nhân

    public ConsentCommandHandler(
        IUserRepository userRepository, ICacheService cacheService, ILegalVersionProvider legal, ITrainingSampleService? trainingSamples = null,
        IAuditLogService? audit = null)
    {
        _audit = audit;
        _userRepository = userRepository;
        _cacheService = cacheService;
        _legal = legal;
        _trainingSamples = trainingSamples;
    }

    public async Task<UserProfileDto> AcceptAsync(AcceptConsentCommand command, CancellationToken ct = default)
    {
        var current = command.Kind switch
        {
            ConsentKind.Terms => await _legal.CurrentTermsVersionAsync(ct),
            ConsentKind.Ai => ConsentVersions.Ai,
            ConsentKind.Training => ConsentVersions.Training,
            ConsentKind.SurveyAi => ConsentVersions.SurveyAi,
            ConsentKind.Community => ConsentVersions.Community,
            _ => throw UnknownKind(),
        };
        if (command.Version != current)
        {
            throw new ConflictException(AuthErrorCodes.ConsentVersionOutdated,
                "Nội dung chính sách đã thay đổi. Hãy cập nhật ứng dụng để xem và đồng ý bản mới.");
        }

        var user = await LoadAsync(command.UserId, ct);
        var now = DateTimeOffset.UtcNow;
        var aiBefore = user.AiConsentVersion;
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
            case ConsentKind.SurveyAi:
                user.SurveyAiConsentVersion = current;
                user.SurveyAiConsentAt = now;
                break;
            case ConsentKind.Community:
                user.CommunityConsentVersion = current;
                user.CommunityConsentAt = now;
                break;
            default:
                user.TrainingConsentVersion = current;
                user.TrainingConsentAt = now;
                break;
        }

        var profile = await SaveAsync(user, ct);
        await LogChangeAsync(user.Id, command.Kind, current, accepted: true, ct);
        if (command.Kind == ConsentKind.Terms && user.AiConsentVersion != aiBefore)
        {
            await LogChangeAsync(user.Id, ConsentKind.Ai, ConsentVersions.Ai, accepted: true, ct);
        }

        return profile;
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
        else if (kind == ConsentKind.SurveyAi)
        {
            // Rút đồng ý: từ đây gợi ý chạy bằng rule. Gợi ý đã tạo giữ nguyên (không có gì ở bên thứ ba để thu hồi — request không mang định danh).
            user.SurveyAiConsentVersion = null;
            user.SurveyAiConsentAt = null;
        }
        else if (kind == ConsentKind.Community)
        {
            user.CommunityConsentVersion = null;
            user.CommunityConsentAt = null;
        }
        else
        {
            user.TrainingConsentVersion = null;
            user.TrainingConsentAt = null;
        }

        var profile = await SaveAsync(user, ct);
        await LogChangeAsync(userId, kind, null, accepted: false, ct);
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

    /// <summary>D3: một dòng audit mỗi lần đồng ý / rút, chỉ loại, phiên bản và chiều; là "lịch sử đồng ý" trong gói dữ liệu cá nhân.</summary>
    private Task LogChangeAsync(Guid userId, ConsentKind kind, int? version, bool accepted, CancellationToken ct)
        => _audit is null
            ? Task.CompletedTask
            : _audit.LogAsync(AuditEvents.ConsentChanged, userId, null, new { type = kind.ToString(), version, accepted }, ct);

    private static BusinessRuleException UnknownKind() =>
        new(AuthErrorCodes.ConsentKindUnknown, "Loại đồng ý không hợp lệ.");

    private async Task<User> LoadAsync(Guid userId, CancellationToken ct) =>
        await _userRepository.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User", userId);

    private async Task<UserProfileDto> SaveAsync(User user, CancellationToken ct)
    {
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _userRepository.UpdateAsync(user, ct);
        await _cacheService.RemoveAsync(CacheKeys.UserProfile(user.Id), ct);
        return UserProfileDto.From(user, await _legal.CurrentTermsVersionAsync(ct));
    }
}
