using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>Tách bill theo nhóm: tiền là số nguyên đồng, tổng các nhóm phải khớp tổng bill tới từng đồng.</summary>
public class ReceiptSplitterTests
{
    private static OcrItem Item(string name, long amount, int? parent = null) => new(name, amount, parent, false);
    private static OcrItem Discount(string name, long amount, int? parent = null) => new(name, amount, parent, true);

    // Mỗi dòng một nhãn; null = unknown. Nhãn của dòng giảm giá/phụ thu bị bỏ qua nên đặt gì cũng được.
    private static List<ClassifiedItem> Classes(params string?[] slugs)
        => slugs.Select((s, i) => new ClassifiedItem(i, s, s is null ? "unknown" : "rule")).ToList();

    [Fact]
    public void Ngo_Gia_bill_with_all_items_food_returns_null_single_group()
    {
        var items = new[]
        {
            Item("Trà Bí Đao Ngô Gia (Vừa 700cc)", 19_000),
            Item("+ Thạch Sương Sáo", 3_000, 0),
            Item("+ Thạch Dừa Nguyên Vị", 3_000, 0),
            Discount("Khách VIP 3K", -3_000),
        };
        ReceiptSplitter.Split(items, Classes("food", "food", "food", null), 22_000).Should().BeNull();
    }

    [Fact]
    public void Two_categories_sum_to_total()
    {
        var items = new[] { Item("Sữa tắm", 120_000), Item("Bánh mì", 30_000), Item("Dầu gội", 80_000) };
        var groups = ReceiptSplitter.Split(items, Classes("beauty", "food", "beauty"), 230_000)!;

        groups.Should().HaveCount(2);
        groups[0].Should().BeEquivalentTo(new ReceiptGroup("beauty", 200_000,
            [new ReceiptGroupItem("Sữa tắm", 120_000), new ReceiptGroupItem("Dầu gội", 80_000)]),
            o => o.WithStrictOrdering());
        groups[1].CategorySlug.Should().Be("food");
        groups[1].AmountCents.Should().Be(30_000);
    }

    [Fact]
    public void Discount_with_Parent_is_charged_to_that_items_group()
    {
        var items = new[] { Item("Sữa tắm", 100_000), Discount("Giảm 20K", -20_000, 0), Item("Bánh mì", 50_000) };
        var groups = ReceiptSplitter.Split(items, Classes("beauty", null, "food"), 130_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("beauty", 80_000L), ("food", 50_000L));
        groups[0].Items.Should().ContainSingle().Which.AmountCents.Should().Be(100_000); // món giữ giá gốc
    }

    [Fact]
    public void Discount_on_a_topping_follows_the_chain_to_the_root_item()
    {
        var items = new[]
        {
            Item("Trà sữa", 30_000), Item("+ Trân châu", 5_000, 0), Item("+ Kem cheese", 5_000, 1),
            Discount("Giảm topping", -2_000, 2), Item("Kẹp tóc", 20_000),
        };
        var groups = ReceiptSplitter.Split(items, Classes("food", "shopping", "shopping", null, "shopping"), 58_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("food", 38_000L), ("shopping", 20_000L));
    }

    [Fact]
    public void Shared_discount_is_split_proportionally_and_remainder_goes_to_the_largest_group()
    {
        // 20.000 : 10.000, giảm 1.000 → -666 / -333, dư -1 dồn nhóm lớn nhất.
        var items = new[] { Item("Bánh", 20_000), Item("Bút", 10_000), Discount("Giảm chung", -1_000) };
        var groups = ReceiptSplitter.Split(items, Classes("food", "education", null), 29_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("food", 19_333L), ("education", 9_667L));
    }

    [Fact]
    public void Shared_discount_over_three_groups_puts_remainder_on_the_largest_group()
    {
        // 10.000 x3, giảm 1.000 → -333 mỗi nhóm, dư -1; hòa nhau thì nhóm xuất hiện trước nhận.
        var items = new[] { Item("A", 10_000), Item("B", 10_000), Item("C", 10_000), Discount("Giảm", -1_000) };
        var groups = ReceiptSplitter.Split(items, Classes("food", "beauty", "education", null), 29_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents))
            .Should().Equal(("beauty", 9_667L), ("education", 9_667L), ("food", 9_666L));
    }

    [Fact]
    public void Shared_discount_remainder_two_goes_to_the_largest_group()
    {
        // 30.000 / 50.000 / 20.000, giảm 1.999 → -599 / -999 / -399 = -1.997, dư -2 dồn nhóm 50.000.
        var items = new[] { Item("A", 30_000), Item("B", 50_000), Item("C", 20_000), Discount("Giảm", -1_999) };
        var groups = ReceiptSplitter.Split(items, Classes("beauty", "food", "education", null), 98_001)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents))
            .Should().Equal(("food", 48_999L), ("beauty", 29_401L), ("education", 19_601L));
    }

    [Fact]
    public void Surcharge_rows_merge_into_the_largest_group()
    {
        var items = new[]
        {
            Item("Bánh mì", 30_000), Item("Sữa tắm", 100_000), Item("Phí giao hàng", 15_000), Item("VAT 8%", 2_000),
        };
        var groups = ReceiptSplitter.Split(items, Classes("food", "beauty", "transport", "transport"), 147_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("beauty", 117_000L), ("food", 30_000L));
        groups.SelectMany(g => g.Items).Select(i => i.Name).Should().Equal("Sữa tắm", "Bánh mì");
    }

    [Fact]
    public void Unknown_items_form_one_group_with_null_slug()
    {
        var items = new[] { Item("Bánh", 40_000), Item("Món lạ 1", 10_000), Item("Món lạ 2", 5_000) };
        var classified = new List<ClassifiedItem>
        {
            new(0, "food", "rule"), new(1, null, "unknown"), new(2, "beauty", "unknown"),
        };
        var groups = ReceiptSplitter.Split(items, classified, 55_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("food", 40_000L), ((string?)null, 15_000L));
        groups[1].Items.Select(i => i.Name).Should().Equal("Món lạ 1", "Món lạ 2");
    }

    [Fact]
    public void Sum_mismatch_returns_null()
    {
        var items = new[] { Item("Bánh", 40_000), Item("Bút", 10_000) };
        ReceiptSplitter.Split(items, Classes("food", "education"), 50_001).Should().BeNull();
    }

    [Fact]
    public void Single_resulting_group_returns_null()
    {
        var items = new[] { Item("Bánh", 40_000), Item("Kẹo", 10_000), Item("Phí giao hàng", 5_000) };
        ReceiptSplitter.Split(items, Classes("food", "food", "transport"), 55_000).Should().BeNull();
    }

    [Fact]
    public void Topping_uses_its_parents_category()
    {
        var items = new[] { Item("Trà sữa", 30_000), Item("+ Trân châu", 5_000, 0), Item("Kẹp tóc", 20_000) };
        var groups = ReceiptSplitter.Split(items, Classes("food", "shopping", "shopping"), 55_000)!;

        groups.Select(g => (g.CategorySlug, g.AmountCents)).Should().Equal(("food", 35_000L), ("shopping", 20_000L));
        groups[0].Items.Select(i => i.Name).Should().Equal("Trà sữa", "+ Trân châu");
    }

    [Fact]
    public void Lengths_differ_returns_null()
    {
        var items = new[] { Item("Bánh", 40_000), Item("Bút", 10_000) };
        ReceiptSplitter.Split(items, Classes("food"), 50_000).Should().BeNull();
    }

    [Fact]
    public void Discount_exceeding_a_groups_gross_returns_null()
    {
        var items = new[] { Item("Bánh", 40_000), Item("Bút", 10_000), Discount("Giảm", -15_000, 1) };
        ReceiptSplitter.Split(items, Classes("food", "education", null), 35_000).Should().BeNull();
    }

    [Fact]
    public void Broken_parent_index_returns_null()
    {
        var items = new[] { Item("Bánh", 40_000), Item("+ Topping", 10_000, 7), Item("Bút", 1_000) };
        ReceiptSplitter.Split(items, Classes("food", "food", "education"), 51_000).Should().BeNull();

        var cycle = new[] { Item("A", 1_000, 1), Item("B", 1_000, 0), Item("C", 1_000) };
        ReceiptSplitter.Split(cycle, Classes("food", "food", "education"), 3_000).Should().BeNull();
    }

    [Fact]
    public void Overflow_returns_null()
    {
        var items = new[] { Item("A", long.MaxValue), Item("B", 1), Item("C", 1) };
        ReceiptSplitter.Split(items, Classes("food", "food", "beauty"), 1).Should().BeNull();
    }

    [Theory]
    [InlineData("VAT 8%", true)]
    [InlineData("Phí giao hàng", true)]
    [InlineData("Thuế GTGT", true)]
    [InlineData("Thue GTGT 10%", true)]
    [InlineData("Phí phục vụ 5%", true)]
    [InlineData("PHI DICH VU", true)]
    [InlineData("Túi nilon", true)]
    [InlineData("Vatican Coffee", false)]
    [InlineData("Thuê xe đạp", false)]
    [InlineData("Trà Bí Đao", false)]
    public void Surcharge_detection(string name, bool expected)
        => ReceiptSplitter.IsSurcharge(Item(name, 1_000)).Should().Be(expected);

    [Fact]
    public void Surcharge_detection_ignores_discounts_and_toppings()
    {
        ReceiptSplitter.IsSurcharge(Discount("Giảm VAT", -1_000)).Should().BeFalse();
        ReceiptSplitter.IsSurcharge(Item("+ VAT", 1_000, 0)).Should().BeFalse();
    }

    public static TheoryData<long[], string?[], long> RandomBills => new()
    {
        { [12_345, 6_789, -1_111, 4_321], ["food", "beauty", null, "food"], 22_344 },
        { [99_999, 1, 1, -7], ["food", "beauty", "education", null], 99_994 },
        { [33_333, 33_333, 33_334, -10_001], ["food", "beauty", "education", null], 89_999 },
        { [7_000, 13_000, 2_500, -999, -1], ["shopping", "food", "shopping", null, null], 21_500 },
        { [1_000_000_000_000, 3, -1_000], ["food", "beauty", null], 999_999_999_003 },
    };

    [Theory]
    [MemberData(nameof(RandomBills))]
    public void Groups_always_sum_to_total_when_split_succeeds(long[] amounts, string?[] slugs, long total)
    {
        var items = amounts.Select((a, i) => a < 0 ? Discount($"Giảm {i}", a) : Item($"Món {i}", a)).ToList();
        var groups = ReceiptSplitter.Split(items, Classes(slugs), total);

        groups.Should().NotBeNull();
        groups!.Sum(g => g.AmountCents).Should().Be(total);
        groups.Should().OnlyContain(g => g.AmountCents > 0);
        groups!.Select(g => g.AmountCents).Should().BeInDescendingOrder();
    }
}
