using System.Text.Json.Serialization;

namespace FinMate.Application.Auth.Commands;

/// <summary>Đọc/ghi dạng chữ ("terms", "ai"; không phân biệt hoa thường) để client không phải biết số thứ tự.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConsentKind
{
    Terms,
    Ai,
    Training,

    /// <summary>Gửi signals suy ra từ khảo sát đầu vào cho AI bên thứ ba để gợi ý cách quản lý tài chính. Tách khỏi <see cref="Ai"/> (tên món trên hóa đơn).</summary>
    SurveyAi,

    /// <summary>Đăng nội dung công khai lên cộng đồng. Rút lại thì không đăng thêm được; bài đã đăng do người dùng tự xóa.</summary>
    Community,
}
