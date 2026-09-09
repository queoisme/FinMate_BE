using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMate.API.Middleware;

/// <summary>
/// Configures ASP.NET Core's built-in rate limiter (no custom middleware class needed —
/// this is a thin extension-method wrapper so the policy definitions have one home).
/// </summary>
public static class RateLimitingMiddleware
{
    public const string DefaultPolicy = "default";
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddFinMateRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(DefaultPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 120,
                    }));

            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 10,
                    }));
        });

        return services;
    }
}
