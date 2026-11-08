using FinMate.Application.Agent;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

public class InMemoryFinancialDataSourceTests
{
    [Fact]
    public async Task CategorySpend_IsReturnedLargestFirst_ThenBySlug_AsTheInterfacePromises()
    {
        var source = new InMemoryFinancialDataSource([], [], 0, categorySpend:
        [
            new CategorySpendVnd("transport", "Di chuyển", 1_000_000),
            new CategorySpendVnd("food", "Ăn uống", 3_000_000),
            new CategorySpendVnd("bills", "Hóa đơn", 1_000_000),
        ]);

        var rows = await source.GetCategorySpendAsync(Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 9));

        rows.Select(r => r.Slug).Should().Equal("food", "bills", "transport");
    }
}
