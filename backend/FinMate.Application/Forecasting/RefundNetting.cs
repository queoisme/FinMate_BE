using FinMate.Domain.Enums;

namespace FinMate.Application.Forecasting;

/// <summary>
/// Dữ liệu thói quen chi tiêu cho dự báo (Phase 81, BR-RF-07, BR-RF-08, D14). Hàm thuần trên các giao dịch đã nạp:
/// <list type="bullet">
/// <item>cặp đảo (khoản chi đã đảo + lệnh đảo) bị loại;</item>
/// <item>khoản chi được hoàn 100% bị loại cùng các khoản hoàn của nó;</item>
/// <item>khoản chi hoàn một phần mang số RÒNG, trừ vào ngày của khoản chi (dự báo nói về thói quen, khác báo cáo ghi nhận ở ngày hoàn);</item>
/// <item>khoản hoàn không bao giờ là thu nhập; hoàn mồ côi (không có khoản gốc) bị loại, không coi là "chi âm".</item>
/// </list>
/// Số dư không đi qua đây: đối chiếu số dư đọc dữ liệu thô.
/// </summary>
public static class RefundNetting
{
    public static IReadOnlyList<ForecastTransaction> ForHabits(IReadOnlyList<ForecastTransaction> rows)
    {
        var refundedByOriginal = rows
            .Where(t => t.Relation == TransactionRelation.Refund && t.RelatedTransactionId is not null)
            .GroupBy(t => t.RelatedTransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.AmountCents));

        var result = new List<ForecastTransaction>(rows.Count);
        foreach (var t in rows)
        {
            if (t.Relation is not null || t.IsReversed)
            {
                continue;
            }

            if (t.Type == TransactionType.Debit && refundedByOriginal.TryGetValue(t.Id, out var refunded))
            {
                var net = t.AmountCents - refunded;
                if (net <= 0)
                {
                    continue;
                }

                result.Add(t with { AmountCents = net });
                continue;
            }

            result.Add(t);
        }

        return result;
    }
}
