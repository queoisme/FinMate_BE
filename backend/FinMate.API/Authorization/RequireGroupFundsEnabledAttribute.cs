using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinMate.API.Authorization;

/// <summary>
/// Khi cờ <c>features.groupFunds</c> tắt, API quỹ nhóm trả 422 <c>GROUP_FUNDS_DISABLED</c>. Luôn đặt SAU <see cref="RequireGroupsEnabledAttribute"/>:
/// cờ nhóm tắt thì người gọi nhận <c>GROUPS_DISABLED</c> trước.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireGroupFundsEnabledAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    /// <summary>Chạy sau bộ lọc cờ nhóm (Order mặc định 0).</summary>
    public int Order => 1;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<ISystemConfigProvider>();
        var features = (await config.GetAsync(context.HttpContext.RequestAborted)).Document.Features;
        if (!features.Groups)
        {
            throw new BusinessRuleException(GroupErrorCodes.Disabled, "Tính năng nhóm hiện chưa được bật.");
        }

        if (!features.GroupFunds)
        {
            throw new BusinessRuleException(GroupErrorCodes.FundsDisabled, "Tính năng quỹ nhóm hiện chưa được bật.");
        }

        await next();
    }
}
