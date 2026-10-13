using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Application.Transactions;

/// <summary>Một món trong nhóm, giữ giá gốc (chưa trừ giảm giá).</summary>
public record ReceiptGroupItem(string Name, long AmountCents);

/// <summary>Một nhóm của bill. <c>AmountCents</c> là tiền thực trả sau giảm giá và phụ thu; slug null = món chưa rõ nhóm.</summary>
public record ReceiptGroup(
    string? CategorySlug, long AmountCents, IReadOnlyList<ReceiptGroupItem> Items, Guid? PossibleDuplicateTransactionId = null);

/// <summary>
/// Tách bill thành các nhóm theo danh mục. BE là nơi cộng tiền, không tin tổng của AI.
///
/// Trả <c>null</c> mỗi khi không chắc: dữ liệu lệch, parent hỏng, nhóm âm, dưới 2 nhóm có danh mục, hay tổng các nhóm
/// không khớp tổng bill tới từng đồng. Null nghĩa là bên gọi quay về đường cũ (phân loại cả bill như trước tính năng này, không có nhóm), nên sai thì im lặng an toàn.
/// </summary>
public static class ReceiptSplitter
{
    // Từ vựng phụ thu phải chứa mọi từ của _SURCHARGE_RE/_PRE_TAX_RE trong ocr-service/items.py (dòng thuế/phí OCR tách ở
    // cuối bill); bài kiểm tra Every_ocr_surcharge_term_is_a_surcharge liệt kê chúng. Khác có chủ đích: "thuê" (đi thuê) không tính.
    // Cụm từ phụ thu, so trên tên đã bỏ dấu, chữ thường.
    private static readonly string[] SurchargePhrases =
        ["phi phuc vu", "phi dich vu", "phi giao hang", "phi dong goi", "tui nilon"];

    // So nguyên từ trên tên chữ thường còn dấu: "thuê" (đi thuê) không phải "thuế", "vatican" không phải "vat".
    // "gtgt" (giá trị gia tăng): OCR hay đọc "Thuế" thành "Thu€" nên chỉ còn "GTGT" nhận ra được dòng thuế.
    private static readonly HashSet<string> SurchargeWords = ["vat", "thuế", "thue", "gtgt"];

    // "service" khớp cả khi OCR dính chữ ("SERVICECHARGE"), giống ocr-service.
    private const string ServiceWord = "service";

    // "Tổng tiền chưa thuế", "trước VAT" là dòng cộng tạm; "gồm VAT", "Tổng cộng (gồm thuế)" là thuế đã nằm trong giá: không phải phụ thu.
    // Đồng bộ với _PRE_TAX_RE của ocr-service/items.py.
    private static readonly HashSet<string> PreTaxWords = ["chua", "truoc", "gom"];

    public static IReadOnlyList<ReceiptGroup>? Split(IReadOnlyList<OcrItem> items, IReadOnlyList<ClassifiedItem> classified, long total)
    {
        if (items.Count == 0 || items.Count != classified.Count)
        {
            return null;
        }

        try
        {
            return SplitCore(items, classified, total);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static bool IsSurcharge(OcrItem item)
    {
        if (item.IsDiscount || item.Parent is not null)
        {
            return false;
        }

        var lower = item.Name.ToLowerInvariant();
        var foldedWords = Words(Fold(lower));
        if (foldedWords.Any(PreTaxWords.Contains))
        {
            return false;
        }

        // Đệm khoảng trắng hai đầu để cụm từ chỉ khớp trọn từ.
        var folded = $" {string.Join(' ', foldedWords)} ";
        return Words(lower).Any(SurchargeWords.Contains)
            || folded.Contains(ServiceWord, StringComparison.Ordinal)
            || SurchargePhrases.Any(p => folded.Contains($" {p} "));
    }

    private sealed class Bucket(string? slug, int firstIndex)
    {
        public string? Slug { get; } = slug;
        public int FirstIndex { get; } = firstIndex;
        public long Gross { get; set; }
        public long Net { get; set; }
        public List<ReceiptGroupItem> Items { get; } = [];
    }

    // Toàn bộ phép cộng tiền chạy trong checked: tràn long thì ném OverflowException, Split bắt và trả null.
    private static IReadOnlyList<ReceiptGroup>? SplitCore(IReadOnlyList<OcrItem> items, IReadOnlyList<ClassifiedItem> classified, long total)
    {
        checked
        {
            var buckets = new List<Bucket>();
            var bucketOfRoot = new Dictionary<int, Bucket>();
            long sharedDiscount = 0, surcharge = 0;

            // Lượt 1: món thường (kể cả phụ kiện) vào nhóm của món gốc.
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsDiscount || IsSurcharge(item))
                {
                    continue;
                }

                var root = Root(items, i);
                if (root < 0 || item.Amount < 0)
                {
                    return null;
                }

                var c = classified[root];
                var slug = c.CategorySlug is null || c.Source == "unknown" ? null : c.CategorySlug;
                var bucket = buckets.FirstOrDefault(b => b.Slug == slug);
                if (bucket is null)
                {
                    bucket = new Bucket(slug, i);
                    buckets.Add(bucket);
                }

                bucket.Gross += item.Amount;
                bucket.Items.Add(new ReceiptGroupItem(item.Name, item.Amount));
                bucketOfRoot[root] = bucket;
            }

            // Cần ít nhất 2 nhóm ĐÃ BIẾT danh mục. "Một danh mục + chưa rõ" là bill một danh mục mà rule sót món:
            // tách ra thì một bill thành hai giao dịch, nên để đường cũ phân loại cả bill.
            if (buckets.Count(b => b.Slug is not null) < 2)
            {
                return null;
            }

            // Lượt 2: giảm giá dính món trừ vào nhóm món đó; giảm giá chung và phụ thu cộng dồn để chia sau.
            foreach (var b in buckets)
            {
                b.Net = b.Gross;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.IsDiscount)
                {
                    if (item.Amount > 0)
                    {
                        return null;
                    }

                    if (item.Parent is null)
                    {
                        sharedDiscount += item.Amount;
                        continue;
                    }

                    var root = Root(items, item.Parent.Value);
                    if (root < 0 || !bucketOfRoot.TryGetValue(root, out var owner))
                    {
                        return null;
                    }

                    owner.Net += item.Amount;
                }
                else if (IsSurcharge(item))
                {
                    surcharge += item.Amount;
                }
            }

            // Nhóm lớn nhất theo tiền gốc, hòa thì nhóm xuất hiện trước (buckets đã theo thứ tự xuất hiện).
            var largest = buckets.Aggregate((a, b) => b.Gross > a.Gross ? b : a);
            var grossTotal = buckets.Sum(b => b.Gross);
            if (sharedDiscount != 0)
            {
                if (grossTotal <= 0)
                {
                    return null;
                }

                // Chia theo tỷ lệ bằng số nguyên (cắt về 0), phần dư dồn nhóm lớn nhất để tổng khớp tuyệt đối.
                long allocated = 0;
                foreach (var b in buckets)
                {
                    var share = (long)((Int128)sharedDiscount * b.Gross / grossTotal);
                    b.Net += share;
                    allocated += share;
                }

                largest.Net += sharedDiscount - allocated;
            }

            largest.Net += surcharge;

            // Nhóm 0đ hay âm không lưu thành giao dịch được: quay về đường cũ.
            if (buckets.Any(b => b.Net <= 0) || buckets.Sum(b => b.Net) != total)
            {
                return null;
            }

            return buckets
                .OrderByDescending(b => b.Net).ThenBy(b => b.FirstIndex)
                .Select(b => new ReceiptGroup(b.Slug, b.Net, b.Items))
                .ToList();
        }
    }

    // Lần theo Parent tới món gốc. -1 nếu chỉ số hỏng, vòng lặp, hoặc gốc là giảm giá/phụ thu.
    private static int Root(IReadOnlyList<OcrItem> items, int index)
    {
        for (var steps = 0; steps <= items.Count; steps++)
        {
            if (index < 0 || index >= items.Count)
            {
                return -1;
            }

            var item = items[index];
            if (item.IsDiscount)
            {
                return -1;
            }

            if (item.Parent is null)
            {
                return IsSurcharge(item) ? -1 : index;
            }

            index = item.Parent.Value;
        }

        return -1;
    }

    private static IEnumerable<string> Words(string s)
        => Regex.Split(s, @"[^\p{L}]+").Where(w => w.Length > 0);

    // Bảng bỏ dấu tiếng Việt (chữ thường). Không dùng string.Normalize vì dự án bật InvariantGlobalization,
    // ở chế độ đó Normalize để nguyên ký tự ngoài ASCII.
    private const string Accented = "àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ";
    private const string Plain    = "aaaaaaaaaaaaaaaaaeeeeeeeeeeeiiiiiooooooooooooooooouuuuuuuuuuuyyyyyd";

    private static string Fold(string lower)
    {
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue; // dấu rời (chuỗi dạng tổ hợp)
            }

            var k = Accented.IndexOf(ch);
            sb.Append(k >= 0 ? Plain[k] : ch);
        }

        return sb.ToString();
    }
}
