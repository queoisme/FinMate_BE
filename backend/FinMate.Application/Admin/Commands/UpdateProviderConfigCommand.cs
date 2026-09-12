using FinMate.Domain.Enums;

namespace FinMate.Application.Admin.Commands;

/// <param name="ProviderKey">
/// Chỉ nhận để TỪ CHỐI khi client gửi giá trị khác giá trị hiện tại. Không có tham số này thì
/// một lần sửa nhầm key sẽ đi qua im lặng và cắt đứt liên kết với provider_patterns bên AI DB.
/// </param>
public record UpdateProviderConfigCommand(
    Guid AdminId,
    Guid ProviderConfigId,
    string? ProviderKey,
    string? DisplayName,
    string? PackageName,
    AccountType? AccountType,
    string? IpAddress);
