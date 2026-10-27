namespace FinMate.Domain.Enums;

/// <summary>Vai trò của phương pháp ĐANG DÙNG. Khác <see cref="FinanceMethodRole"/>: không có "Both" — người dùng phải đang dùng nó làm chính hoặc bổ trợ.</summary>
public enum UserMethodRole
{
    Primary,
    Supporting,
}
