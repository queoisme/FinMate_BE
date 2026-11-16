using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

/// <summary>
/// Phần "vì sao" của bản nháp và giao dịch: kênh nhập, câu giải thích và độ tin cậy. Chỉ dựng từ tín hiệu có thật (nguồn danh mục AI Service báo,
/// điểm số của lần quét/thông báo); không có căn cứ thì để null thay vì bịa số.
/// </summary>
public static class DraftInsights
{
    public const string ChatChannel = "Chat";
    public const string VoiceChannel = "Voice";

    public static SourceDetailDto Chat { get; } = new(ChatChannel, "Nhập bằng chat");
    public static SourceDetailDto Voice { get; } = new(VoiceChannel, "Nhập bằng giọng nói");
    public static SourceDetailDto Receipt { get; } = new("Receipt", "Quét hóa đơn");

    public static SourceDetailDto Of(TransactionSource source) => source switch
    {
        TransactionSource.Notification => new("Notification", "Thông báo ngân hàng"),
        TransactionSource.Voice => Voice,
        TransactionSource.Receipt => Receipt,
        TransactionSource.Support => new("Support", "Nhân viên hỗ trợ tạo hộ"),
        _ => new("Manual", "Nhập tay"),
    };

    /// <summary>Câu giải thích vì sao chọn danh mục, theo nguồn mà AI Service báo (memory | brand | keyword | product | model | none).</summary>
    public static string CategoryReason(string? source, bool hasCategory)
    {
        if (!hasCategory)
        {
            return "Chưa đủ thông tin để chọn danh mục, bạn chọn giúp mình nhé.";
        }

        return source switch
        {
            "memory" => "Bạn từng chọn danh mục này cho nơi chi tiêu này nên mình dùng lại.",
            "brand" => "Mình nhận ra thương hiệu và biết nó thuộc danh mục này.",
            "keyword" => "Nội dung có từ khóa gợi ý danh mục này.",
            "product" => "Tên món hàng thuộc danh mục này.",
            "items" => "Các món trên hóa đơn chủ yếu thuộc danh mục này.",
            "model" => "Mô hình phân loại đoán danh mục này từ nội dung.",
            _ => "Mình đoán danh mục này từ nội dung.",
        };
    }

    public static (string Explanation, DraftConfidenceDto Confidence) ForChat(ChatParseEntry e, AssistantSettings cfg)
    {
        double? category = e.CategorySlug is not null && e.CategorySource is { } s
            && cfg.CategorySourceConfidence.TryGetValue(s, out var c) ? c : null;
        // Số tiền do luật bóc từ chữ số/cụm số người dùng nói, không phải mô hình đoán (CLAUDE.md): chắc khi có số.
        double? amount = e.AmountCents > 0 ? 1.0 : null;
        double? when = e.OccurredAt is not null ? 1.0 : null;
        var known = new[] { amount, category }.Where(v => v is not null).Select(v => v!.Value).ToList();
        double? overall = known.Count == 0 ? null : known.Min();

        var parts = new List<string>();
        if (e.AmountCents > 0)
        {
            parts.Add("Số tiền lấy trực tiếp từ câu bạn nhập.");
        }
        if (e.Kind != "transfer")
        {
            parts.Add(CategoryReason(e.CategorySource, e.CategorySlug is not null));
        }

        return (string.Join(" ", parts), new DraftConfidenceDto(overall, amount, category, null, when));
    }

    public static (string Explanation, DraftConfidenceDto Confidence) ForAnalyze(AnalyzeResponse r)
    {
        var amount = r.Extraction?.AmountCents is null ? null : r.Extraction.Confidence;
        var category = r.Categorization?.CategorySlug is null ? null : r.Categorization.Confidence;
        var known = new[] { amount, category }.Where(v => v is not null).Select(v => v!.Value).ToList();
        double? overall = known.Count == 0 ? null : known.Min();
        var explanation = r.Categorization?.CategorySlug is null
            ? "Chưa đủ thông tin để chọn danh mục, bạn chọn giúp mình nhé."
            : "Mô hình phân loại đoán danh mục này từ nội dung, số tiền được bóc bằng luật từ chính câu bạn nhập.";
        return (explanation, new DraftConfidenceDto(overall, amount, category, null, null));
    }

    public static (string? Explanation, DraftConfidenceDto Confidence) ForReceipt(
        ScannedReceiptDto d, string? categorySource)
    {
        if (d.AmountCents is null)
        {
            return (null, new DraftConfidenceDto(d.Confidence, null, null, null, null));
        }

        var merchant = string.IsNullOrWhiteSpace(d.MerchantName) ? "" : " Tên cửa hàng đọc từ đầu hóa đơn.";
        var text = $"Số tiền lấy từ tổng cộng trên hóa đơn.{merchant} " + CategoryReason(categorySource, d.CategorySlug is not null);
        return (text.Trim(), new DraftConfidenceDto(d.Confidence, d.Confidence, null, null, null));
    }

    /// <summary>Lập luận của AI cho giao dịch đã lưu, dựng từ kết quả thông báo (null nếu không có).</summary>
    public static AiClassificationDto? ForNotification(AiResult? r)
    {
        if (r is null)
        {
            return null;
        }

        var known = new[] { r.ExtractionConfidence, r.CategorizationConfidence }.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (known.Count == 0)
        {
            return null;
        }

        var reason = "Bóc số tiền từ thông báo ngân hàng bằng luật. "
            + (r.CategorySlug is null ? CategoryReason(null, false) : CategoryReason("model", true));
        return new AiClassificationDto(known.Min(), reason, r.CategorizerVersion);
    }

    public static AiClassificationDto? ForScan(ReceiptScan? scan)
        => scan?.Confidence is { } c
            ? new AiClassificationDto(c, "Số tiền lấy từ tổng cộng trên hóa đơn. " + CategoryReason(scan.CategorySource, scan.CategorySlug is not null), scan.OcrEngine)
            : null;
}
