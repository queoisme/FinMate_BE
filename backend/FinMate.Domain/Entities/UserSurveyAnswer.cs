namespace FinMate.Domain.Entities;

/// <summary>
/// Một câu trả lời khảo sát của người dùng. Lưu theo MÃ chữ (<see cref="QuestionCode"/>,
/// <see cref="OptionCode"/>) chứ không FK: admin sửa hay tắt câu hỏi thì câu trả lời cũ vẫn đọc được.
/// Xóa cứng được — người dùng có quyền xóa câu trả lời của mình.
/// </summary>
public class UserSurveyAnswer
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string QuestionCode { get; set; } = string.Empty;
    public string OptionCode { get; set; } = string.Empty;
    public DateTimeOffset AnsweredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
