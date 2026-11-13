using FinMate.Application.Admin.SystemConfig;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>Nhóm cấu hình <c>transfers</c> và <c>snapshots</c> (Phase 83): mặc định đã chốt, khoảng hợp lệ, audit gọi đúng tên nhóm.</summary>
public class TransferSettingsTests
{
    private readonly SystemConfigDocumentValidator _validator = new();

    [Fact]
    public void Defaults_AreTheAgreedValues()
    {
        var doc = new SystemConfigDocument();

        doc.Transfers.PairWindowMinutes.Should().Be(30);
        doc.Transfers.ClockSkewMinutes.Should().Be(2);
        doc.Transfers.MaxFeeCents.Should().Be(20_000);
        doc.Transfers.MaxFeePercent.Should().Be(2);
        doc.Transfers.ProviderAliases["vietcombank"].Should().Contain("vcb");
        doc.Transfers.ProviderAliases["momo"].Should().Contain("momo");
        doc.Snapshots.RetentionDays.Should().Be(365);
        _validator.Validate(doc).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 2, 20_000, 2, false)]
    [InlineData(240, 2, 20_000, 2, true)]
    [InlineData(241, 2, 20_000, 2, false)]
    [InlineData(30, 11, 20_000, 2, false)]
    [InlineData(30, 0, 0, 0, true)]
    [InlineData(30, 2, 200_001, 2, false)]
    [InlineData(30, 2, 20_000, 11, false)]
    public void TransferValues_MustStayInRange(int window, int skew, long maxFee, int maxPercent, bool valid)
    {
        var doc = new SystemConfigDocument
        {
            Transfers = new TransferSettings { PairWindowMinutes = window, ClockSkewMinutes = skew, MaxFeeCents = maxFee, MaxFeePercent = maxPercent },
        };

        _validator.Validate(doc).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("v")]
    [InlineData("")]
    public void AnAliasMustBeAtLeastTwoCharacters(string alias)
    {
        var doc = new SystemConfigDocument
        {
            Transfers = new TransferSettings { ProviderAliases = new Dictionary<string, string[]> { ["momo"] = [alias] } },
        };

        _validator.Validate(doc).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    public void SnapshotRetention_MustStayInRange(int days, bool valid)
    {
        _validator.Validate(new SystemConfigDocument { Snapshots = new SnapshotSettings { RetentionDays = days } }).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("transfers")]
    [InlineData("snapshots")]
    public void ChangedSections_NamesTheNewGroups(string section)
    {
        var before = new SystemConfigDocument();
        var after = section == "transfers"
            ? before with { Transfers = new TransferSettings { PairWindowMinutes = 45 } }
            : before with { Snapshots = new SnapshotSettings { RetentionDays = 90 } };

        SystemConfigService.ChangedSections(before, after).Should().Equal(section);
    }
}
