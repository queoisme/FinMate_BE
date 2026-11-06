using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Community;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinMate.API.Authorization;

/// <summary>Khi cờ <c>features.community</c> tắt, API cộng đồng trả 422. Chặn ở server để app cũ hoặc lời gọi trực tiếp không lọt qua lúc đang tắt vì sự cố.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireCommunityEnabledAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<ISystemConfigProvider>();
        var document = (await config.GetAsync(context.HttpContext.RequestAborted)).Document;
        if (!document.Features.Community)
        {
            throw new BusinessRuleException(CommunityErrorCodes.Disabled, "Tính năng cộng đồng chưa được bật.");
        }

        await next();
    }
}
