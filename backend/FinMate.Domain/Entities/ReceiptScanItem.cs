namespace FinMate.Domain.Entities;

/// <summary>
/// Một dòng món đã bóc tách của một lần quét hóa đơn, kèm nhóm AI xếp. Chỉ lưu tên + tiền + nhóm, không lưu chữ OCR gốc.
/// </summary>
public class ReceiptScanItem
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }

    /// <summary>Thứ tự dòng trên hóa đơn (duy nhất trong một lần quét).</summary>
    public int Position { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Có thể âm: dòng giảm giá.</summary>
    public long AmountCents { get; set; }

    /// <summary>Null khi AI không xếp được món này vào nhóm nào.</summary>
    public string? CategorySlug { get; set; }

    public string? CategorySource { get; set; }

    /// <summary>Dòng giảm giá dính ngay dưới một món: vị trí của món đó.</summary>
    public int? ParentPosition { get; set; }
}
