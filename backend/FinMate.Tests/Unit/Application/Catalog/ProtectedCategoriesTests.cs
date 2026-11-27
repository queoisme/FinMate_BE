using FinMate.Application.Catalog;
using FinMate.Application.Categories.Commands;
using FinMate.Application.Loans;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Refunds;
using FinMate.Application.Transactions.Transfers;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Catalog;

/// <summary>S4: danh sách slug được bảo vệ phủ mọi hằng slug mà code tra danh mục hệ thống theo đó. Thêm hằng mới mà quên ở đây thì test này phải được sửa cùng.</summary>
public class ProtectedCategoriesTests
{
    [Fact]
    public void EverySlugTheCodeLooksUpIsProtected()
    {
        string[] lookedUp =
        [
            RefundPolicy.IncomeSlug,
            TrainingSampleService.IncomeSlug,
            LoanTransactionLink.InterestCategoryForLend,
            Category.LoanSlug,
            TransferFee.CategorySlug,
            LoanTransactionLink.InterestCategoryForBorrow,
            SetCategoryHiddenCommandHandler.FallbackSlug,
        ];

        lookedUp.Should().OnlyContain(slug => ProtectedCategories.IsProtected(slug));
        ProtectedCategories.Slugs.Should().BeEquivalentTo("income", "loan", "other");
    }

    [Fact]
    public void OrdinaryCategoriesAreNotProtected()
        => ProtectedCategories.IsProtected("food").Should().BeFalse();
}
