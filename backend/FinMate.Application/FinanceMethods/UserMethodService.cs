using FinMate.Application.Budgets;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.FinanceMethods;

public class UserMethodService : IUserMethodService
{
    private readonly IFinanceMethodRepository _methods;
    private readonly IUserMethodRepository _userMethods;

    public UserMethodService(IFinanceMethodRepository methods, IUserMethodRepository userMethods)
    {
        _methods = methods;
        _userMethods = userMethods;
    }

    public async Task<IReadOnlyList<MethodDto>> GetCatalogAsync(CancellationToken ct = default)
        => (await _methods.GetMethodsAsync(includeInactive: false, ct))
            .OrderBy(m => m.SortOrder)
            .Select(FinanceMapper.ToDto)
            .ToList();

    public async Task<IReadOnlyList<UserMethodDto>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await _userMethods.GetActiveAsync(userId, ct);
        var methods = (await _methods.GetMethodsByIdsAsync(active.Select(a => a.MethodId), ct)).ToDictionary(m => m.Id);

        return active
            .Where(a => methods.ContainsKey(a.MethodId))
            .OrderBy(a => a.Role)   // 'primary' đứng trước 'supporting'
            .Select(a => FinanceMapper.ToDto(a, methods[a.MethodId]))
            .ToList();
    }

    public async Task<UserMethodDto> ChooseAsync(
        Guid userId, string methodCode, UserMethodRole role, Guid? recommendationId, CancellationToken ct = default)
    {
        var method = await _methods.GetMethodByCodeAsync(methodCode, ct) ?? throw new NotFoundException("FinanceMethod", methodCode);
        if (!method.IsActive)
        {
            throw new BusinessRuleException(FinanceErrorCodes.MethodInactive, "Cách quản lý này hiện không còn được cung cấp.");
        }

        var allowed = role == UserMethodRole.Primary ? MethodSelector.CanBePrimary(method.Role) : MethodSelector.CanBeSupport(method.Role);
        if (!allowed)
        {
            throw new BusinessRuleException(
                FinanceErrorCodes.MethodRoleMismatch,
                role == UserMethodRole.Primary
                    ? "Cách này chỉ dùng được như công cụ bổ trợ, không làm cách quản lý chính được."
                    : "Cách này chỉ dùng được làm cách quản lý chính, không làm công cụ bổ trợ được.");
        }

        var active = await _userMethods.GetActiveAsync(userId, ct);
        if (active.FirstOrDefault(a => a.Role == role && a.MethodId == method.Id) is { } same)
        {
            return FinanceMapper.ToDto(same, method);   // chọn lại đúng cái đang dùng: không làm gì (idempotent)
        }

        if (active.Any(a => a.MethodId == method.Id))
        {
            throw new ConflictException(
                FinanceErrorCodes.MethodAlreadyInUse, "Cách này đang được dùng ở vai trò còn lại. Hãy kết thúc nó trước nếu muốn đổi vai trò.");
        }

        var recommendation = recommendationId is { } id ? await _userMethods.GetRecommendationAsync(id, userId, ct) : null;
        var now = DateTimeOffset.UtcNow;

        var chosen = new UserMethod
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MethodId = method.Id,
            MethodVersion = method.Version,
            Role = role,
            Params = "{}",
            RecommendationId = recommendation?.Id,
            ChosenVia = WasRecommended(recommendation, method.Id, role) ? MethodChoiceSource.Recommended : MethodChoiceSource.Manual,
            Status = UserMethodStatus.Active,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var previous = active.FirstOrDefault(a => a.Role == role);
        if (previous is not null)
        {
            previous.Status = UserMethodStatus.Ended;
            previous.EndedAt = now;
            previous.UpdatedAt = now;
        }

        await _userMethods.ReplaceActiveAsync(previous, chosen, ct);

        // Ghi lại người dùng nhận hay đổi so với gợi ý — chỉ khi chọn cách CHÍNH (bổ trợ là phần thêm, không phải câu trả lời cho gợi ý).
        if (recommendation is not null && role == UserMethodRole.Primary && recommendation.Outcome == RecommendationOutcome.Shown)
        {
            recommendation.Outcome = recommendation.PrimaryMethodId == method.Id ? RecommendationOutcome.Accepted : RecommendationOutcome.Changed;
            recommendation.DecidedAt = now;
            await _userMethods.UpdateRecommendationAsync(recommendation, ct);
        }

        return FinanceMapper.ToDto(chosen, method);
    }

    public async Task<UserMethodDto> UpdateParamsAsync(
        Guid userId, Guid userMethodId, IReadOnlyDictionary<string, long?> changes, CancellationToken ct = default)
    {
        var userMethod = await _userMethods.GetByIdAsync(userMethodId, userId, ct) ?? throw new NotFoundException("UserMethod", userMethodId);
        if (userMethod.Status != UserMethodStatus.Active)
        {
            throw new ConflictException(FinanceErrorCodes.MethodParamInvalid, "Cách quản lý này đã kết thúc, không chỉnh được nữa.");
        }

        var method = await _methods.GetMethodByIdAsync(userMethod.MethodId, ct) ?? throw new NotFoundException("FinanceMethod", userMethod.MethodId);
        var defs = MethodParams.ParseDefs(method.ParamDefs);
        var overrides = MethodParams.ParseOverrides(userMethod.Params);

        foreach (var (key, value) in changes)
        {
            var def = defs.FirstOrDefault(d => d.Key == key)
                ?? throw new BusinessRuleException(FinanceErrorCodes.MethodParamInvalid, $"Tham số '{key}' không tồn tại ở cách quản lý này.");

            if (value is null)
            {
                overrides.Remove(key);
                continue;
            }

            if (!def.UserEditable)
            {
                throw new BusinessRuleException(FinanceErrorCodes.MethodParamInvalid, $"Tham số '{key}' không cho người dùng chỉnh.");
            }

            if (!MethodParams.IsAllowed(def, value.Value))
            {
                throw new BusinessRuleException(
                    FinanceErrorCodes.MethodParamInvalid, $"Tham số '{key}' phải nằm trong khoảng từ {def.Min} đến {def.Max}.");
            }

            overrides[key] = value.Value;
        }

        // T1 và T2 chỉnh riêng từng cái nhưng phải đúng thứ tự khi đứng cạnh nhau; kiểm trên giá trị HIỆU LỰC (mặc định nếu chưa ghi đè).
        var effective = MethodParams.Effective(defs, overrides);
        if (effective.TryGetValue(FinanceParamKeys.T1Pct, out var t1) && effective.TryGetValue(FinanceParamKeys.T2Pct, out var t2)
            && !AlertThresholds.IsValid(new AlertThresholds((int)t1, (int)t2)))
        {
            throw new BusinessRuleException(
                FinanceErrorCodes.MethodParamInvalid, "Ngưỡng nhắc nhẹ (T1) phải nhỏ hơn ngưỡng cảnh báo mạnh (T2), và T2 tối đa 99%.");
        }

        userMethod.Params = FinanceJson.Serialize(overrides);
        userMethod.UpdatedAt = DateTimeOffset.UtcNow;
        await _userMethods.UpdateAsync(userMethod, ct);

        return FinanceMapper.ToDto(userMethod, method);
    }

    public async Task EndAsync(Guid userId, Guid userMethodId, CancellationToken ct = default)
    {
        var userMethod = await _userMethods.GetByIdAsync(userMethodId, userId, ct) ?? throw new NotFoundException("UserMethod", userMethodId);
        if (userMethod.Status == UserMethodStatus.Ended)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        userMethod.Status = UserMethodStatus.Ended;
        userMethod.EndedAt = now;
        userMethod.UpdatedAt = now;
        await _userMethods.UpdateAsync(userMethod, ct);
    }

    private static bool WasRecommended(MethodRecommendation? recommendation, Guid methodId, UserMethodRole role)
        => recommendation is not null
           && (role == UserMethodRole.Primary ? recommendation.PrimaryMethodId == methodId : recommendation.SupportMethodId == methodId);
}
