using System.Security.Claims;
using FinMate.API.Controllers;
using FinMate.Application.Common.Models;
using FinMate.Application.Subscriptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace FinMate.API.Authorization;

/// <summary>
/// Chặn endpoint theo gói. Giữ một lượt trước khi chạy và trả lại nếu action lỗi, để lỗi AI hay OCR không ăn lượt của khách.
/// Giữ trước chứ không đếm sau, vì đếm sau thì nhiều request song song cùng lọt qua khi chỉ còn một lượt.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireSubscriptionFeatureAttribute : Attribute, IAsyncActionFilter
{
    private const int FirstErrorStatus = 400;
    private const string ClientRequestIdArgument = "clientRequestId";

    public RequireSubscriptionFeatureAttribute(string feature)
    {
        Feature = feature;
    }

    public string Feature { get; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var entitlements = http.RequestServices.GetRequiredService<ISubscriptionEntitlementService>();
        var userId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var reservation = await entitlements.ReserveAsync(userId, Feature, ClientRequestIdOf(context), http.RequestAborted);
        var executed = await next();

        if (reservation is not null && !Delivered(executed))
        {
            // Không dùng RequestAborted: request bị hủy cũng phải trả lại lượt.
            await entitlements.ReleaseAsync(reservation, CancellationToken.None);
        }
    }

    private static Guid? ClientRequestIdOf(ActionExecutingContext context)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is ChatRequest { ClientRequestId: { } fromBody })
            {
                return fromBody;
            }
        }

        return context.ActionArguments.TryGetValue(ClientRequestIdArgument, out var fromForm) ? fromForm as Guid? : null;
    }

    /// <summary>Ảnh không đọc được vẫn trả 200 kèm kết quả rỗng; khách không nhận được gì nên không tính lượt.</summary>
    private static bool Delivered(ActionExecutedContext executed)
    {
        if (executed.Exception is not null || executed.Result is IStatusCodeActionResult { StatusCode: >= FirstErrorStatus })
        {
            return false;
        }

        return executed.Result is not ObjectResult { Value: ApiResponse<ScannedReceiptDto> { Data: { AmountCents: null, Orders: null } } };
    }
}
