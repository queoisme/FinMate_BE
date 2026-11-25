using System.Globalization;
using FinMate.Application.Common;
using FinMate.Application.Reports;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.DataExport;

/// <summary>
/// Định dạng dòng giao dịch trong CSV (Phase 91, BR-EXP-02/04). Dùng chung cho <c>GET /exports/transactions.csv</c> và gói dữ liệu đầy đủ.
/// Cột 17 "Số tiền tính vào chi tiêu" có dấu (D5): cộng cột này của một kỳ ra đúng tổng chi của báo cáo, vì nó đi qua đúng các luật của báo cáo
/// (<see cref="NetFlow"/>, <see cref="LoanExclusion"/>, <c>Transfer</c> không là chi, chỉ <c>Confirmed</c>) chứ không viết lại.
/// </summary>
public static class TransactionCsvFormat
{
    public const int FormatVersion = 1;

    private static readonly string[] Columns =
    [
        "Ngày", "Thời điểm", "Loại", "Số tiền", "Ví", "Ví đích", "Danh mục", "Nơi chi", "Ghi chú", "Nguồn", "Trạng thái", "Quan hệ",
        "Liên quan đến", "Gộp từ", "Nhóm", "Tính vào chi tiêu", "Số tiền tính vào chi tiêu", "Mã giao dịch",
    ];

    private const string DeletedColumn = "Đã xoá lúc";

    public static IReadOnlyList<string> Header(bool deletedFile)
        => deletedFile ? [.. Columns, DeletedColumn] : Columns;

    public static IReadOnlyList<CsvCell> HeaderCells(bool deletedFile) => Header(deletedFile).Select(CsvCell.Text).ToList();

    public static IReadOnlyList<CsvCell> ToCells(TransactionExportRow row, bool deletedFile)
    {
        var effect = deletedFile && row.DeletedAt is not null ? 0 : SpendingEffect(row);
        var (relation, relatedId) = Relation(row);
        var cells = new List<CsvCell>(Columns.Length + 1)
        {
            CsvCell.Text(VietnamTime.DateOf(row.TransactedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            CsvCell.Text(Iso(row.TransactedAt)),
            CsvCell.Text(TypeName(row.Type)),
            CsvCell.Number(row.AmountCents),
            CsvCell.Text(row.AccountName),
            CsvCell.Text(row.CounterAccountName),
            CsvCell.Text(row.CategoryName),
            CsvCell.Text(row.MerchantName),
            CsvCell.Text(row.Description),
            CsvCell.Text(SourceName(row.Source)),
            CsvCell.Text(row.Status == TransactionStatus.Confirmed ? "Đã xác nhận" : "Chờ duyệt"),
            CsvCell.Text(relation),
            CsvCell.Text(relatedId?.ToString()),
            CsvCell.Text(MergedSourcesText(row.MergedSources)),
            CsvCell.Text(row.GroupName),
            CsvCell.Text(effect != 0 ? "Có" : "Không"),
            CsvCell.Number(effect),
            CsvCell.Text(row.Id.ToString()),
        };
        if (deletedFile)
        {
            cells.Add(CsvCell.Text(row.DeletedAt is { } deleted ? Iso(deleted) : null));
        }

        return cells;
    }

    /// <summary>Phần đóng góp có dấu của giao dịch vào tổng chi của báo cáo: chi được tính → +, hoàn tiền → −, còn lại 0.</summary>
    public static long SpendingEffect(TransactionExportRow row)
    {
        if (row.Status != TransactionStatus.Confirmed || row.Type == TransactionType.Transfer || row.CategorySlug == Category.LoanSlug)
        {
            return 0;
        }

        return NetFlow.Of(row.Type, row.Relation, row.IsReversed, row.AmountCents).SpentCents;
    }

    public static string Iso(DateTimeOffset instant)
        => VietnamTime.ToLocal(instant).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>D11: một giá trị theo thứ tự ưu tiên, kèm id tương ứng.</summary>
    private static (string? Name, Guid? Id) Relation(TransactionExportRow row)
    {
        if (row.MergedIntoTransactionId is { } survivor)
        {
            return ("Đã gộp vào", survivor);
        }

        if (row.Relation == TransactionRelation.Reversal)
        {
            return ("Đảo giao dịch", row.RelatedTransactionId);
        }

        if (row.IsReversed)
        {
            return ("Đã bị đảo", row.ReversedByTransactionId);
        }

        if (row.Relation == TransactionRelation.Refund)
        {
            return ("Hoàn tiền", row.RelatedTransactionId);
        }

        if (row.IsLoanInterest)
        {
            return ("Lãi vay", row.LoanId);
        }

        if (row.LoanId is not null || row.CategorySlug == Category.LoanSlug)
        {
            return ("Vay nợ", row.LoanId);
        }

        if (row.GroupFundEntryId is { } entry)
        {
            return ("Quỹ nhóm", entry);
        }

        return row.DuplicateStatus == DuplicateStatus.Suspected ? ("Nghi trùng", row.DuplicateOfTransactionId) : (null, null);
    }

    private static string TypeName(TransactionType type) => type switch
    {
        TransactionType.Debit => "Chi",
        TransactionType.Credit => "Thu",
        _ => "Chuyển ví",
    };

    /// <summary>D1: giao dịch chat đã lưu mang <c>Manual</c>, nên "Tay" gồm cả gõ form và chat.</summary>
    private static string SourceName(TransactionSource source) => source switch
    {
        TransactionSource.Notification => "Thông báo",
        TransactionSource.Voice => "Giọng nói",
        TransactionSource.Receipt => "Hoá đơn",
        TransactionSource.Support => "Hỗ trợ",
        _ => "Tay",
    };

    private static string? MergedSourcesText(IReadOnlyList<string>? sources)
        => sources is null || sources.Count == 0
            ? null
            : string.Join("; ", sources.Select(s => Enum.TryParse<TransactionSource>(s, true, out var parsed) ? SourceName(parsed) : s));
}
