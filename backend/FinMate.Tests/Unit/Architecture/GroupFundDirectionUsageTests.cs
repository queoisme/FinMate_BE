using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Architecture;

/// <summary>
/// Phase 85, S6: chỉ <c>TransferLegs</c> được đọc hướng của transfer gắn quỹ để tính số dư. Mã khác hiểu mọi Transfer là "tiền rời ví nguồn", nên
/// một chỗ mới tự đọc cột này (hoặc bỏ qua nó) dễ làm sai dấu khoản rút từ quỹ mà không báo lỗi. Danh sách dưới là nơi được phép NHẮC tới nó:
/// định nghĩa, ánh xạ DB, nơi chỉ chuyển giá trị cho <c>TransferLegs</c>, và nơi tạo giao dịch gắn quỹ.
/// </summary>
public class GroupFundDirectionUsageTests
{
    private static readonly string[] Allowed =
    [
        "FinMate.Domain/Enums/GroupFundEnums.cs",
        "FinMate.Domain/Entities/Transaction.cs",
        "FinMate.Application/Transactions/TransferLegs.cs",
        "FinMate.Application/Transactions/Transfers/MergeSnapshots.cs",          // chỉ ghi lại trạng thái trước
        "FinMate.Application/Transactions/Duplicates/DuplicateMatcher.cs",       // chỉ chuyển cho TransferLegs
        "FinMate.Infrastructure/Persistence/Configurations/TransactionConfiguration.cs",
        "FinMate.Infrastructure/Persistence/Repositories/TransactionRepository.cs", // dựng probe
        "FinMate.Infrastructure/Persistence/Migrations/",
    ];

    /// <summary>Nơi tạo hoặc đổi giao dịch gắn quỹ (dịch vụ quỹ nhóm) được đặt hướng.</summary>
    private const string FundServices = "FinMate.Application/Groups/Funds/";

    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "FinMate.Application")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục backend.");
    }

    [Fact]
    public void OnlyTransferLegs_ReadsTheFundDirection()
    {
        var root = BackendRoot();
        var offenders = new[] { "FinMate.Domain", "FinMate.Application", "FinMate.Infrastructure", "FinMate.API" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/obj/") && !path.Contains("/bin/"))
            .Where(path => !Allowed.Any(path.StartsWith) && !path.StartsWith(FundServices))
            .Where(path => File.ReadAllText(Path.Combine(root, path)).Contains("GroupFundDirection"))
            .ToList();

        offenders.Should().BeEmpty("hướng tiền của transfer gắn quỹ chỉ được đọc qua TransferLegs");
    }
}
