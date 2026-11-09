using System.Security.Cryptography;
using System.Text;

namespace FinMate.Application.Subscriptions;

/// <summary>Chữ ký form thanh toán SePay. Thứ tự trường là của SePay, không được sắp xếp lại: chuỗi ký phải trùng ký tự với phía họ.</summary>
public static class SePaySignature
{
    public const string FieldName = "signature";

    private static readonly string[] SignedFields =
    [
        "order_amount", "merchant", "currency", "operation", "order_description", "order_invoice_number",
        "customer_id", "payment_method", "success_url", "error_url", "cancel_url",
    ];

    /// <summary>SePay bỏ qua trường rỗng, <c>0</c> và <c>"0"</c> khi kiểm chữ ký, nên ở đây cũng phải bỏ.</summary>
    public static string BuildSignedString(IReadOnlyDictionary<string, string> fields)
        => string.Join(',', SignedFields
            .Where(name => fields.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) && value != "0")
            .Select(name => $"{name}={fields[name]}"));

    public static string Sign(IReadOnlyDictionary<string, string> fields, string secretKey)
        => Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(BuildSignedString(fields))));
}
