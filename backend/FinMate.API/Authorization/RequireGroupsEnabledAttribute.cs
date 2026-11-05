using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinMate.API.Authorization;

/// <summary>Khi cờ <c>features.groups</c> tắt, API nhóm trả 422. Chặn ở server để app cũ hoặc lời gọi trực tiếp không lọt qua khi đang tắt vì sự cố.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireGroupsEnabledAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<ISystemConfigProvider>();
        var document = (await config.GetAsync(context.HttpContext.RequestAborted)).Document;
        if (!document.Features.Groups)
        {
            throw new BusinessRuleException(GroupErrorCodes.Disabled, "Tính năng nhóm hiện chưa được bật.");
        }

        await next();
    }
}
