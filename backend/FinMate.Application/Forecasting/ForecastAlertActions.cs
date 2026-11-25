using System.Text.Json;
using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

public static class ForecastAlertActionKinds
{
    /// <summary>Nâng hạn mức của ngân sách đang có nguy cơ vượt lên mức dự kiến (<c>AmountCents</c>, <c>CategoryId</c> rỗng = ngân sách tổng).</summary>
    public const string RaiseBudgetLimit = "raise_budget_limit";

    /// <summary>Mở màn dự báo (điều hướng, không đổi dữ liệu).</summary>
    public const string OpenForecast = "open_forecast";

    /// <summary>Theo dõi lại một khoản định kỳ đã bị tạm ngừng vì quá hạn lâu (<c>TargetId</c> = id khoản đó).</summary>
    public const string ResumeRecurring = "resume_recurring";
}

/// <param name="AmountCents">Số tiền của hành động (đồng); null nếu loại này không có.</param>
/// <param name="CategoryId">Nhóm của hạn mức; chuỗi rỗng nghĩa là ngân sách tổng. Null nếu loại này không có.</param>
/// <param name="TargetId">Đối tượng bị tác động (vd khoản định kỳ).</param>
public sealed record ForecastAlertActionDto(string Kind, string Title, long? AmountCents, string? CategoryId, string? TargetId);

/// <summary>
/// Nút hành động đi kèm cảnh báo dự báo, để người dùng xử lý ngay thay vì chỉ đọc. Hoàn toàn do CODE tính từ loại cảnh báo và payload đã lưu (không LLM, không bảng mới),
/// nên cảnh báo cũ chưa có đủ trường thì đơn giản là không có nút. Chỉ là đề xuất: app gọi API sẵn có khi người dùng bấm, cảnh báo không tự đổi dữ liệu nào.
/// </summary>
public static class ForecastAlertActions
{
    /// <summary>Nâng hạn mức làm tròn LÊN chục nghìn đồng, để con số đề xuất đủ chứa mức dự kiến.</summary>
    private const long Rounding = 10_000;

    public static IReadOnlyList<ForecastAlertActionDto> For(ForecastAlertKind kind, JsonElement data)
    {
        switch (kind)
        {
            case ForecastAlertKind.BudgetRisk:
                var actions = new List<ForecastAlertActionDto>();
                if (Long(data, "projectedCents") is { } projected && projected > 0 && Text(data, "categoryId") is { } categoryId)
                {
                    var raised = (projected + Rounding - 1) / Rounding * Rounding;
                    if (Long(data, "limitCents") is not { } limit || raised > limit)
                    {
                        actions.Add(new ForecastAlertActionDto(
                            ForecastAlertActionKinds.RaiseBudgetLimit, $"Nâng hạn mức lên {ForecastText.Money(raised)}", raised, categoryId, null));
                    }
                }

                actions.Add(new ForecastAlertActionDto(ForecastAlertActionKinds.OpenForecast, "Xem dự báo ngân sách", null, null, null));
                return actions;

            case ForecastAlertKind.WeeklyDigest:
                return [new ForecastAlertActionDto(ForecastAlertActionKinds.OpenForecast, "Xem dự báo ngân sách", null, null, null)];

            case ForecastAlertKind.LowBalance:
                return [new ForecastAlertActionDto(ForecastAlertActionKinds.OpenForecast, "Xem dự báo dòng tiền", null, null, null)];

            case ForecastAlertKind.BillStatusCheck when Text(data, "recurringItemId") is { Length: > 0 } itemId:
                return [new ForecastAlertActionDto(ForecastAlertActionKinds.ResumeRecurring, "Vẫn dùng, theo dõi lại", null, null, itemId)];

            default:
                return [];
        }
    }

    private static string? Text(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Long(JsonElement data, string name)
        => Text(data, name) is { } text && long.TryParse(text, out var number) ? number : null;
}
