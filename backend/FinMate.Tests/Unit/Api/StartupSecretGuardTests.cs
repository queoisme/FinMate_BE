using FinMate.API.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinMate.Tests.Unit.Api;

/// <summary>
/// Giá trị "change-me..." nằm công khai trong `.env.example` ĐÃ COMMIT, nên một `JWT_SECRET`
/// còn nguyên nghĩa là ai đọc repo cũng tự ký được token admin — mà mọi thứ vẫn chạy bình
/// thường, không có triệu chứng nào.
/// </summary>
public class StartupSecretGuardTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void APlaceholderSecretStopsTheProcessStarting()
    {
        var configuration = Config(
            ("JWT_SECRET", "change-me-to-a-random-64-char-minimum-secret-string"),
            ("AI_SERVICE_API_KEY", "a-real-key"));

        var act = () => StartupSecretGuard.ThrowIfPlaceholdersRemain(configuration, "Production");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT_SECRET*");
    }

    [Fact]
    public void TheMessageNamesEveryOffendingVariable()
    {
        // Báo từng cái một là bắt người triển khai sửa, chạy lại, rồi phát hiện cái tiếp theo.
        var configuration = Config(
            ("JWT_SECRET", "change-me"),
            ("AI_SERVICE_API_KEY", "change-me"),
            ("HANGFIRE_DASHBOARD_PASS", "change-me-local-dev"));

        var act = () => StartupSecretGuard.ThrowIfPlaceholdersRemain(configuration, "Production");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
                .Contain("JWT_SECRET").And
                .Contain("AI_SERVICE_API_KEY").And
                .Contain("HANGFIRE_DASHBOARD_PASS");
    }

    [Fact]
    public void RealSecretsStartFine()
    {
        var configuration = Config(
            ("JWT_SECRET", "GVn3k9x-real-random-secret"),
            ("AI_SERVICE_API_KEY", "another-real-key"),
            ("ADMIN_SEED_PASSWORD", "AnotherRealPassword1!"),
            ("HANGFIRE_DASHBOARD_PASS", "AlsoReal2!"));

        var act = () => StartupSecretGuard.ThrowIfPlaceholdersRemain(configuration, "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void AnAbsentVariableIsNotTreatedAsAPlaceholder()
    {
        // Thiếu biến là chuyện của chỗ khác — mỗi biến bắt buộc đã có kiểm riêng ở Program.cs,
        // và báo sai loại lỗi chỉ dẫn người ta đi nhầm hướng.
        var act = () => StartupSecretGuard.ThrowIfPlaceholdersRemain(Config(), "Production");

        act.Should().NotThrow();
    }

    [Fact]
    public void TheCheckIsCaseInsensitive()
    {
        var configuration = Config(("JWT_SECRET", "CHANGE-ME-PLEASE"));

        var act = () => StartupSecretGuard.ThrowIfPlaceholdersRemain(configuration, "Staging");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Staging*");
    }

    [Theory]
    [InlineData("change-me-to-a-random-64-char-minimum-secret")]
    [InlineData("change-me-local-dev")]
    [InlineData("ChangeMe123!")]
    [InlineData("CHANGEME")]
    [InlineData("change_me_please")]
    public void EverySpellingOfThePlaceholderIsCaught(string value)
    {
        // `.env.example` dùng CẢ HAI kiểu: "change-me-local-dev" và "ChangeMe123!". Bản đầu
        // chỉ tìm chuỗi "change-me" nên bỏ lọt mật khẩu admin — một tài khoản quản trị với
        // mật khẩu nằm công khai trong repo mà vẫn khởi động được.
        StartupSecretGuard.IsPlaceholder(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("GVn3k9x-real-random-secret")]
    [InlineData("exchange-market-data")]
    [InlineData("")]
    [InlineData(null)]
    public void RealValuesArePassedThrough(string? value)
    {
        StartupSecretGuard.IsPlaceholder(value).Should().BeFalse();
    }

    [Fact]
    public void TheSeededAdminPasswordFromTheExampleFileIsRejected()
    {
        // Chính giá trị đang nằm trong backend/.env.example đã commit.
        var configuration = Config(("ADMIN_SEED_PASSWORD", "ChangeMe123!"));

        var act = () => StartupSecretGuard.ThrowIfPlaceholdersRemain(configuration, "Production");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ADMIN_SEED_PASSWORD*");
    }
}
