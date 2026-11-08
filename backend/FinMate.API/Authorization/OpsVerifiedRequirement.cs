using FinMate.Application.Ops;
using Microsoft.AspNetCore.Authorization;

namespace FinMate.API.Authorization;

/// <summary>Token phải đã qua bước OTP. Tài khoản nội bộ xem được dữ liệu của nhiều người nên mật khẩu một mình là không đủ.</summary>
public sealed class OpsVerifiedRequirement : IAuthorizationRequirement;

public sealed class OpsVerifiedHandler : AuthorizationHandler<OpsVerifiedRequirement>
{
    private readonly OpsAuthOptions _options;

    public OpsVerifiedHandler(OpsAuthOptions options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OpsVerifiedRequirement requirement)
    {
        if (!_options.RequireTwoFactor || context.User.HasClaim(OpsClaims.Verified, OpsClaims.VerifiedValue))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
