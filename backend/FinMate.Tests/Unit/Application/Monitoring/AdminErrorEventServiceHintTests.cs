using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Monitoring;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Monitoring;

public class AdminErrorEventServiceHintTests
{
    private static readonly IReadOnlyList<ErrorHint> Defaults = new MonitoringSettings().ErrorHints;

    [Theory]
    [InlineData("FinMate.Application.Common.Exceptions.AIServiceUnavailableException", "Dịch vụ AI không phản hồi")]
    [InlineData("System.OperationCanceledException", "Yêu cầu bị hủy giữa chừng")]
    [InlineData("Swashbuckle.AspNetCore.SwaggerGen.SwaggerGeneratorException", "Không dựng được trang tài liệu API")]
    public void FindHint_ReadableTitleForKnownErrors(string type, string title)
        => AdminErrorEventService.FindHint(Defaults, type)!.Title.Should().Be(title);

    [Fact]
    public void FindHint_UnknownOrMissingType_ReturnsNull_SoUiFallsBackToShortName()
    {
        AdminErrorEventService.FindHint(Defaults, "Some.Unknown.WeirdException").Should().BeNull();
        AdminErrorEventService.FindHint(Defaults, null).Should().BeNull();
    }

    [Fact]
    public void FindHint_UsesAdminEditedList()
    {
        var custom = new[] { new ErrorHint { Match = "weird", Title = "Lạ", Explanation = "x" } };
        AdminErrorEventService.FindHint(custom, "Some.Unknown.WeirdException")!.Title.Should().Be("Lạ");
    }

    [Fact]
    public void ShortType_DropsNamespace()
        => AdminErrorEventService.ShortType("System.OperationCanceledException").Should().Be("OperationCanceledException");

    [Fact]
    public void DefaultHints_PassValidation()
        => new SystemConfigDocumentValidator().Validate(new SystemConfigDocument()).IsValid.Should().BeTrue();
}
