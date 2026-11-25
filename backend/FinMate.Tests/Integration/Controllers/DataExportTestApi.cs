using System.Net;
using System.Text.Json;
using FinMate.Application.Auth.Otp;
using FinMate.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Các bước lặp lại của test gói dữ liệu (Phase 91): xin mã, gửi yêu cầu, đọc trạng thái.</summary>
internal static class DataExportTestApi
{
    public const string Base = "/api/v1/exports/personal-data";

    /// <summary>Xin mã. Bỏ thời gian chờ gửi lại trước (1 phút) để một test xin được nhiều mã liên tiếp.</summary>
    public static async Task<string> OtpAsync(this LoanTestApi api, DataExportApiFactory factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOtpService>().ClearCooldownAsync(api.Email, OtpPurpose.DataExport);
        }

        (await api.SendAsync(HttpMethod.Post, $"{Base}/otp")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return factory.Email.CodeFor(api.Email) ?? throw new InvalidOperationException("Không có email chứa mã.");
    }

    public static async Task<Guid> RequestExportAsync(this LoanTestApi api, DataExportApiFactory factory)
    {
        var code = await api.OtpAsync(factory);
        var data = await api.ExpectAsync(HttpMethod.Post, Base, new { code }, HttpStatusCode.Accepted);
        data.GetProperty("status").GetString().Should().Be("Pending");
        return data.GetProperty("id").GetGuid();
    }

    public static Task<JsonElement> ExportAsync(this LoanTestApi api, Guid id) => api.ExpectAsync(HttpMethod.Get, $"{Base}/{id}", null, HttpStatusCode.OK);

    /// <summary>Đặt thẳng trạng thái trong DB (cho các test hạn mức, không cần chạy job).</summary>
    public static Task SetStatusAsync(this LoanTestApi api, Guid id, PersonalDataExportStatus status, string? failureCode = null, bool counts = true)
        => api.InDbAsync(async db =>
        {
            var row = await db.PersonalDataExports.SingleAsync(e => e.Id == id);
            row.Status = status;
            row.FailureCode = failureCode;
            row.CountsTowardLimit = counts;
            if (status is PersonalDataExportStatus.Ready or PersonalDataExportStatus.Expired)
            {
                row.ReadyAt ??= DateTimeOffset.UtcNow;
                row.ExpiresAt ??= DateTimeOffset.UtcNow.AddHours(72);
            }

            return await db.SaveChangesAsync();
        });

    /// <summary>Chạy job dựng gói như Hangfire sẽ chạy.</summary>
    public static async Task RunJobAsync(this DataExportApiFactory factory, Guid exportId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<FinMate.Application.DataExport.PersonalDataExportRunner>().RunAsync(exportId);
    }
}
