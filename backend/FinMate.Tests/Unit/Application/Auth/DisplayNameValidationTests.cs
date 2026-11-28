using FinMate.Application.Auth.Commands;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Auth;

/// <summary>
/// Tên hiển thị được in lại ở web admin, nhật ký, email, export và app mobile. Web admin đã
/// escape, nhưng các nơi khác không thuộc quyền ta; ký tự &lt; và &gt; không phải phần của một
/// cái tên nên chặn ngay từ lúc ghi.
/// </summary>
public class DisplayNameValidationTests
{
    private static readonly RegisterCommandValidator Register = new();
    private static readonly UpdateUserProfileCommandValidator Profile = new();

    [Theory]
    [InlineData("<img src=x onerror=1>")]
    [InlineData("A<script>alert(1)</script>")]
    [InlineData("Tên >")]
    public void RegisterRejectsAngleBrackets(string name)
    {
        var result = Register.Validate(new RegisterCommand("a@b.co", "Password123!", name));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCommand.DisplayName));
    }

    [Theory]
    [InlineData("<b>x</b>")]
    [InlineData("a > b")]
    public void ProfileUpdateRejectsAngleBrackets(string name)
    {
        var result = Profile.Validate(new UpdateUserProfileCommand(Guid.NewGuid(), name));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateUserProfileCommand.DisplayName));
    }

    [Theory]
    [InlineData("Nguyễn Văn Á")]
    [InlineData("O'Brien")]
    [InlineData("Trần Thị B. (kế toán)")]
    public void OrdinaryNamesStillPass(string name)
    {
        Register.Validate(new RegisterCommand("a@b.co", "Password123!", name)).IsValid.Should().BeTrue();
        Profile.Validate(new UpdateUserProfileCommand(Guid.NewGuid(), name)).IsValid.Should().BeTrue();
    }
}
