using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Models;
using FluentValidation;

namespace FinMate.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (FinMateException ex)
        {
            await WriteErrorResponseAsync(context, ex.HttpStatusCode, ex.ErrorCode, ex.Message, null);
        }
        catch (ValidationException ex)
        {
            var details = ex.Errors
                .Select(e => new ApiErrorDetail(e.PropertyName, e.ErrorMessage))
                .ToList();
            await WriteErrorResponseAsync(context, 400, "VALIDATION_ERROR", "Dữ liệu không hợp lệ.", details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteErrorResponseAsync(context, 500, "INTERNAL_ERROR",
                "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.", null);
        }
    }

    private static async Task WriteErrorResponseAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message,
        IReadOnlyList<ApiErrorDetail>? details)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var response = ApiResponse<object>.Fail(new ApiError(errorCode, message, details));
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        await context.Response.WriteAsync(json);
    }
}
