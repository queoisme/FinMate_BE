using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// Danh sách danh mục gửi cho ai-service khi tách hóa đơn: danh mục hệ thống chi tiêu trước, rồi danh mục riêng của người dùng,
/// tối đa 60 (ai-service trả 422 khi vượt, cả lần tách mất). Không có danh mục riêng chi tiêu thì trả null (đường cũ).
/// </summary>
public class UserCategoryChoicesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _userId = Guid.NewGuid();

    private static Category Sys(string slug, string kind = Category.KindExpense) => new()
    {
        Id = Guid.NewGuid(), Slug = slug, Name = slug.ToUpperInvariant(), IsSystem = true, Kind = kind, IsActive = true, CreatedAt = Now.AddYears(-1),
    };

    private Category Own(string slug, Category? parent = null, bool favorite = false, int ageDays = 0, Guid? owner = null, string name = "")
        => new()
        {
            Id = Guid.NewGuid(), UserId = owner ?? _userId, Slug = slug, Name = name.Length > 0 ? name : slug, IsActive = true, IsFavorite = favorite,
            Kind = Category.KindExpense, ParentCategoryId = parent?.Id, CreatedAt = Now.AddDays(-ageDays),
        };

    [Fact]
    public void Build_ReturnsNull_WhenTheUserHasNoOwnExpenseCategory()
    {
        var visible = new List<Category> { Sys("food"), Sys("shopping") };

        UserCategoryChoices.Build(visible, _userId).Should().BeNull();
    }

    [Fact]
    public void Build_ReturnsNull_WhenTheOnlyOwnCategoriesAreIncomeLoanInactiveOrBlank()
    {
        var income = Own("luong");
        income.Kind = Category.KindIncome;
        var inactive = Own("cu");
        inactive.IsActive = false;
        var visible = new List<Category>
        {
            Sys("food"), income, inactive, Own(Category.LoanSlug), Own("trong", name: "  "),
        };

        UserCategoryChoices.Build(visible, _userId).Should().BeNull();
    }

    [Fact]
    public void Build_ExcludesIncomeAndLoan()
    {
        var ownIncome = Own("thuong");
        ownIncome.Kind = Category.KindIncome;
        var visible = new List<Category>
        {
            Sys("food"), Sys("income", Category.KindIncome), Sys("salary", Category.KindIncome), Sys(Category.LoanSlug),
            Own("uong"), ownIncome, Own(Category.LoanSlug),
        };

        var result = UserCategoryChoices.Build(visible, _userId)!;

        result.Select(c => c.Slug).Should().Equal("food", "uong");
        result.Select(c => c.IsSystem).Should().Equal(true, false);
    }

    [Fact]
    public void Build_ExcludesTheIncomeSlugEvenWhenItsKindSaysExpense()
    {
        var visible = new List<Category> { Sys("food"), Sys("income"), Own("uong") };

        UserCategoryChoices.Build(visible, _userId)!.Select(c => c.Slug).Should().Equal("food", "uong");
    }

    [Fact]
    public void Build_MapsParentSlugFromTheSystemParent()
    {
        var food = Sys("food");
        var shopping = Sys("shopping");
        var uong = Own("uong", food);
        var rieng = Own("rieng");
        var visible = new List<Category> { food, shopping, uong, rieng };

        var result = UserCategoryChoices.Build(visible, _userId)!;

        result.Select(c => (c.Slug, c.ParentSlug)).Should().Equal(("food", null), ("shopping", null), ("uong", "food"), ("rieng", null));
        result.Single(c => c.Slug == "uong").Id.Should().Be(uong.Id);
        result.Single(c => c.Slug == "uong").Name.Should().Be("uong");
    }

    [Fact]
    public void Build_LeavesParentSlugNullWhenTheParentIsNotInTheList()
    {
        var income = Sys("salary", Category.KindIncome);
        var visible = new List<Category> { Sys("food"), income, Own("uong", income) };

        UserCategoryChoices.Build(visible, _userId)!.Single(c => c.Slug == "uong").ParentSlug.Should().BeNull();
    }

    [Fact]
    public void Build_IgnoresOtherUsersCategories()
    {
        var other = Guid.NewGuid();
        var visible = new List<Category> { Sys("food"), Own("uong"), Own("cua-nguoi-khac", owner: other) };

        UserCategoryChoices.Build(visible, _userId)!.Select(c => c.Slug).Should().Equal("food", "uong");
    }

    [Fact]
    public void Build_ReturnsNull_WhenOnlyAnotherUserOwnsCategories()
    {
        var visible = new List<Category> { Sys("food"), Own("cua-nguoi-khac", owner: Guid.NewGuid()) };

        UserCategoryChoices.Build(visible, _userId).Should().BeNull();
    }

    [Fact]
    public void Build_SkipsCategoriesWithABlankName()
    {
        var blankSystem = Sys("trong");
        blankSystem.Name = " ";
        var visible = new List<Category> { Sys("food"), blankSystem, Own("uong"), Own("ten-rong", name: "   ") };

        UserCategoryChoices.Build(visible, _userId)!.Select(c => c.Slug).Should().Equal("food", "uong");
    }

    [Fact]
    public void Build_CapsAt60_KeepingSystemFirstThenFavoritesThenNewest()
    {
        var system = Enumerable.Range(0, 10).Select(i => Sys($"sys{i:00}")).ToList();
        var own = new List<Category>();
        for (var i = 0; i < 70; i++)
        {
            // c00-c04 yêu thích nhưng rất cũ; còn lại càng chỉ số nhỏ càng mới.
            own.Add(Own($"c{i:00}", favorite: i < 5, ageDays: i < 5 ? 1000 + i : i));
        }

        var visible = system.Concat(own.AsEnumerable().Reverse()).Concat([Sys("income", Category.KindIncome), Sys(Category.LoanSlug)]).ToList();

        var result = UserCategoryChoices.Build(visible, _userId)!;

        result.Should().HaveCount(UserCategoryChoices.MaxCategories);
        result.Take(10).Select(c => c.Slug).Should().BeEquivalentTo(system.Select(s => s.Slug));
        result.Take(10).Should().OnlyContain(c => c.IsSystem);
        result.Skip(10).Take(5).Select(c => c.Slug).Should().BeEquivalentTo("c00", "c01", "c02", "c03", "c04");
        // 45 chỗ còn lại: các danh mục KHÔNG yêu thích mới nhất (c05..c49).
        result.Skip(15).Select(c => c.Slug).Should().BeEquivalentTo(Enumerable.Range(5, 45).Select(i => $"c{i:00}"));
        result.Select(c => c.Slug).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_WhenSystemCategoriesAloneExceedTheCap_KeepsTheFirst60BySlugAndNeverNamesAMissingParent()
    {
        var system = Enumerable.Range(0, 70).Select(i => Sys($"s{i:00}")).Reverse().ToList();
        var parentBeyondCap = system.Single(s => s.Slug == "s69");
        var visible = system.Concat([Own("uong", parentBeyondCap)]).ToList();

        var result = UserCategoryChoices.Build(visible, _userId)!;

        result.Should().HaveCount(UserCategoryChoices.MaxCategories);
        result.Select(c => c.Slug).Should().Equal(Enumerable.Range(0, 60).Select(i => $"s{i:00}"));
        result.Should().OnlyContain(c => c.IsSystem && c.ParentSlug == null);  // chỗ đã hết nên danh mục riêng (cha s69 bị cắt) không được gửi, không slug nào mồ côi
    }

    [Fact]
    public void SystemSlugOf_MapsAnOwnCategoryToItsSystemParent_AndLeavesTheRestAlone()
    {
        var food = Sys("food");
        var visible = new List<Category> { food, Sys("shopping"), Own("uong", food), Own("rieng") };

        UserCategoryChoices.SystemSlugOf("uong", visible, _userId).Should().Be("food");
        UserCategoryChoices.SystemSlugOf("rieng", visible, _userId).Should().BeNull();
        UserCategoryChoices.SystemSlugOf("shopping", visible, _userId).Should().Be("shopping");
        UserCategoryChoices.SystemSlugOf("khong-co", visible, _userId).Should().Be("khong-co");
        UserCategoryChoices.SystemSlugOf(null, visible, _userId).Should().BeNull();
        UserCategoryChoices.SystemSlugOf("uong", null, _userId).Should().Be("uong");
    }
}
