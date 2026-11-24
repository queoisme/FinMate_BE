using System.Reflection;
using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application;

/// <summary>Mọi mã sự kiện nhật ký đều có câu tiếng Việt, để trang nhật ký không hiện "Hoạt động khác" cho sự kiện thật.</summary>
public class AuditEventLabelsTests
{
    [Fact]
    public void EveryAuditEventHasALabel()
    {
        var events = typeof(AuditEvents)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        events.Should().NotBeEmpty();
        events.Except(AuditEventLabels.Known).Should().BeEmpty("mỗi sự kiện mới trong AuditEvents cần một câu trong AuditEventLabels");
    }

    [Fact]
    public void UnknownCodesAreNotGivenAnInventedMeaning()
        => AuditEventLabels.Describe("Something.Unheard.Of").Should().Be("Hoạt động khác");
}
