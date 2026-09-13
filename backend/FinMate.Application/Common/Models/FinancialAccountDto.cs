using FinMate.Domain.Enums;

namespace FinMate.Application.Common.Models;

public record FinancialAccountDto(
    Guid Id,
    string AccountType,
    string AccountName,
    string? PackageName,
    bool IsMonitored,
    long BalanceCents,
    string? ProviderDisplayName,
    DateTimeOffset CreatedAt);

public record AccountBalanceDto(Guid AccountId, long BalanceCents, DateTimeOffset AsOf);

/// <summary>
/// Một app tài chính người dùng có thể tick chọn ở màn onboarding (docx Bước 1.2).
///
/// Có <c>PackageName</c> vì client Android cần chính giá trị đó để dựng bộ lọc Tầng 1
/// (<c>onNotificationPosted</c> so <c>sbn.packageName</c>) — không có nó thì app không biết
/// thông báo nào đáng giữ lại và thông báo nào drop ngay trên RAM.
/// </summary>
public record ProviderOptionDto(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    string PackageName,
    AccountType AccountType);
