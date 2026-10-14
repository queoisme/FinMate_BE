using System.Text.Json.Serialization;

namespace FinMate.Application.Auth.Commands;

/// <summary>Đọc/ghi dạng chữ ("terms", "ai"; không phân biệt hoa thường) để client không phải biết số thứ tự.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConsentKind
{
    Terms,
    Ai,
    Training,
}
