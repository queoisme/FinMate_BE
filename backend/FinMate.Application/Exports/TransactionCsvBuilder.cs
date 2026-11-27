using System.Globalization;
using System.Text;
using FinMate.Application.Common;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Exports;

/// <summary>
/// Dựng tệp CSV giao dịch (thuần, không đụng DB). Hai điểm dễ sai:
/// ô văn bản bắt đầu bằng <c>= + - @</c> bị Excel hiểu là công thức nên phải chặn bằng dấu nháy đơn (mô tả giao dịch do người dùng/thông báo ngân hàng
/// quyết định, không tin được); và Excel đặt vùng Việt Nam tách cột bằng <c>;</c> nên có chế độ dấu chấm phẩy.
/// </summary>
public static class TransactionCsvBuilder
{
    public const string Comma = "comma";
    public const string Semicolon = "semicolon";

    private static readonly string[] Header =
    [
        "Ngày giờ", "Loại", "Số tiền (đ)", "Danh mục", "Ví", "Ví đối ứng", "Nơi nhận/Cửa hàng", "Mô tả",
    ];

    public static char DelimiterOf(string? name) => string.Equals(name, Semicolon, StringComparison.OrdinalIgnoreCase) ? ';' : ',';

    /// <param name="accountNames">Tên ví theo id (ví đã xoá mềm thì không có trong từ điển; hiện "(ví đã xoá)").</param>
    public static string Build(IEnumerable<Transaction> rows, IReadOnlyDictionary<Guid, string> accountNames, char delimiter)
    {
        var text = new StringBuilder();
        text.Append('﻿');   // BOM: Excel cần để đọc đúng tiếng Việt UTF-8
        AppendLine(text, Header, delimiter);

        foreach (var t in rows)
        {
            var local = VietnamTime.ToLocal(t.TransactedAt);
            AppendLine(text,
            [
                local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                TypeLabel(t.TransactionType),
                SignedAmount(t).ToString(CultureInfo.InvariantCulture),
                t.Category?.Name ?? string.Empty,
                Name(accountNames, t.FinancialAccountId),
                t.CounterAccountId is { } counter ? Name(accountNames, counter) : string.Empty,
                t.MerchantName ?? string.Empty,
                t.Description ?? string.Empty,
            ], delimiter, textColumnsFrom: 3);
        }

        return text.ToString();
    }

    /// <summary>Chi âm, thu dương, chuyển ví dương (tiền rời ví nguồn là số dương ở dòng chuyển; người đọc nhìn cột "Loại").</summary>
    internal static long SignedAmount(Transaction t) => t.TransactionType == TransactionType.Debit ? -t.AmountCents : t.AmountCents;

    private static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Debit => "Chi tiêu",
        TransactionType.Credit => "Thu nhập",
        TransactionType.Transfer => "Chuyển tiền",
        _ => type.ToString(),
    };

    private static string Name(IReadOnlyDictionary<Guid, string> names, Guid id)
        => names.TryGetValue(id, out var name) ? name : "(ví đã xoá)";

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string> cells, char delimiter, int textColumnsFrom = int.MaxValue)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(delimiter);
            }

            sb.Append(Escape(i >= textColumnsFrom ? NeutralizeFormula(cells[i]) : cells[i], delimiter));
        }

        sb.Append("\r\n");
    }

    internal static string NeutralizeFormula(string value)
        => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    internal static string Escape(string value, char delimiter)
    {
        var needsQuotes = value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
