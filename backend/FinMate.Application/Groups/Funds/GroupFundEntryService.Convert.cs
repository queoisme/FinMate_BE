using FinMate.Domain.Entities;

namespace FinMate.Application.Groups.Funds;

public partial class GroupFundEntryService
{
    /// <summary>S4a: chuyển một khoản chi có sẵn thành khoản chi từ quỹ. Làm ở Task 7b.</summary>
    private static Task ConvertSourceAsync(GroupFundEntry entry, Guid sourceId, CreateGroupFundEntryCommand command, DateTimeOffset now, CancellationToken ct)
        => throw Invalid("Chưa hỗ trợ tạo khoản chi từ một giao dịch có sẵn.");
}
