using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;
using static FinMate.Tests.Unit.Application.FinanceMethods.FinanceTestData;

namespace FinMate.Tests.Unit.Application.FinanceMethods;

/// <summary>Chọn, đổi, chỉnh tham số (gồm T1/T2) và kết thúc cách quản lý đang dùng.</summary>
public class UserMethodServiceTests
{
    private readonly Mock<IFinanceMethodRepository> _methods = new();
    private readonly Mock<IUserMethodRepository> _userMethods = new();
    private readonly UserMethodService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly FinanceMethod _cashFlow = Method("cash_flow", FinanceMethodRole.Primary, paramDefs: AlertParams(), version: 3);
    private readonly FinanceMethod _tool = Method("tool", FinanceMethodRole.Supporting);
    private readonly FinanceMethod _both = Method("both", FinanceMethodRole.Both);
    private readonly List<UserMethod> _active = [];

    public UserMethodServiceTests()
    {
        foreach (var m in new[] { _cashFlow, _tool, _both })
        {
            _methods.Setup(r => r.GetMethodByCodeAsync(m.Code, It.IsAny<CancellationToken>())).ReturnsAsync(m);
            _methods.Setup(r => r.GetMethodByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);
        }

        _methods.Setup(r => r.GetMethodsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => new[] { _cashFlow, _tool, _both }.Where(m => ids.Contains(m.Id)).ToList());
        _userMethods.Setup(r => r.GetActiveAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_active);

        _service = new UserMethodService(_methods.Object, _userMethods.Object);
    }

    private UserMethod Active(FinanceMethod method, UserMethodRole role, string paramsJson = "{}")
    {
        var userMethod = new UserMethod
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            MethodId = method.Id,
            MethodVersion = method.Version,
            Role = role,
            Params = paramsJson,
            Status = UserMethodStatus.Active,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _active.Add(userMethod);
        _userMethods.Setup(r => r.GetByIdAsync(userMethod.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(userMethod);
        return userMethod;
    }

    // ---- chọn ----

    [Fact]
    public async Task Choose_StoresTheMethodVersion_AndDefaultsToNoOverrides()
    {
        var chosen = await _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Primary, null);

        chosen.Method.Code.Should().Be("cash_flow");
        chosen.Overrides.Should().BeEmpty();
        chosen.Effective["t1_pct"].Should().Be(70);
        chosen.ChosenVia.Should().Be("manual");
        _userMethods.Verify(r => r.ReplaceActiveAsync(null, It.Is<UserMethod>(u => u.MethodVersion == 3 && u.Params == "{}"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Choose_ReplacesTheMethodHoldingTheSameRole_ByEndingItInTheSameCall()
    {
        var previous = Active(_both, UserMethodRole.Primary);

        await _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Primary, null);

        previous.Status.Should().Be(UserMethodStatus.Ended);
        previous.EndedAt.Should().NotBeNull();
        _userMethods.Verify(r => r.ReplaceActiveAsync(previous, It.IsAny<UserMethod>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Choose_AcceptingTheRecommendedPrimary_MarksItRecommendedAndAccepted()
    {
        var recommendation = new MethodRecommendation { Id = Guid.NewGuid(), UserId = _userId, PrimaryMethodId = _cashFlow.Id, Outcome = RecommendationOutcome.Shown };
        _userMethods.Setup(r => r.GetRecommendationAsync(recommendation.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(recommendation);

        var chosen = await _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Primary, recommendation.Id);

        chosen.ChosenVia.Should().Be("recommended");
        recommendation.Outcome.Should().Be(RecommendationOutcome.Accepted);
        recommendation.DecidedAt.Should().NotBeNull();
        _userMethods.Verify(r => r.UpdateRecommendationAsync(recommendation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Choose_AnotherMethodThanRecommended_MarksTheRecommendationChanged_AndTheChoiceManual()
    {
        var recommendation = new MethodRecommendation { Id = Guid.NewGuid(), UserId = _userId, PrimaryMethodId = _both.Id, Outcome = RecommendationOutcome.Shown };
        _userMethods.Setup(r => r.GetRecommendationAsync(recommendation.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(recommendation);

        var chosen = await _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Primary, recommendation.Id);

        chosen.ChosenVia.Should().Be("manual");
        recommendation.Outcome.Should().Be(RecommendationOutcome.Changed);
    }

    [Fact]
    public async Task Choose_ASupportingTool_DoesNotAnswerTheRecommendation()
    {
        var recommendation = new MethodRecommendation
        {
            Id = Guid.NewGuid(), UserId = _userId, PrimaryMethodId = _cashFlow.Id, SupportMethodId = _tool.Id, Outcome = RecommendationOutcome.Shown,
        };
        _userMethods.Setup(r => r.GetRecommendationAsync(recommendation.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(recommendation);

        var chosen = await _service.ChooseAsync(_userId, "tool", UserMethodRole.Supporting, recommendation.Id);

        chosen.ChosenVia.Should().Be("recommended", "cách bổ trợ này đúng là cái được gợi ý");
        recommendation.Outcome.Should().Be(RecommendationOutcome.Shown, "chọn công cụ bổ trợ chưa phải câu trả lời cho gợi ý về cách chính");
    }

    [Fact]
    public async Task Choose_AToolAsPrimary_IsRefused()
    {
        var act = () => _service.ChooseAsync(_userId, "tool", UserMethodRole.Primary, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.MethodRoleMismatch);
    }

    [Fact]
    public async Task Choose_APrimaryOnlyMethodAsSupport_IsRefused()
    {
        var act = () => _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Supporting, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.MethodRoleMismatch);
    }

    [Fact]
    public async Task Choose_TheSameMethodAndRoleAgain_IsANoOp()
    {
        var existing = Active(_cashFlow, UserMethodRole.Primary);

        var chosen = await _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Primary, null);

        chosen.Id.Should().Be(existing.Id);
        _userMethods.Verify(r => r.ReplaceActiveAsync(It.IsAny<UserMethod?>(), It.IsAny<UserMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Choose_AMethodAlreadyUsedInTheOtherRole_Conflicts()
    {
        Active(_both, UserMethodRole.Supporting);

        var act = () => _service.ChooseAsync(_userId, "both", UserMethodRole.Primary, null);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.MethodAlreadyInUse);
    }

    [Fact]
    public async Task Choose_AnInactiveOrUnknownMethod_IsRefused()
    {
        _cashFlow.IsActive = false;

        var inactive = () => _service.ChooseAsync(_userId, "cash_flow", UserMethodRole.Primary, null);
        (await inactive.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.MethodInactive);

        _methods.Setup(r => r.GetMethodByCodeAsync("ghost", It.IsAny<CancellationToken>())).ReturnsAsync((FinanceMethod?)null);
        var unknown = () => _service.ChooseAsync(_userId, "ghost", UserMethodRole.Primary, null);
        await unknown.Should().ThrowAsync<NotFoundException>();
    }

    // ---- tham số ----

    [Fact]
    public async Task UpdateParams_StoresOnlyTheOverrides_AndReportsTheEffectiveValues()
    {
        var mine = Active(_cashFlow, UserMethodRole.Primary);

        var result = await _service.UpdateParamsAsync(_userId, mine.Id, new Dictionary<string, long?> { ["t1_pct"] = 55 });

        result.Overrides.Should().Equal(new Dictionary<string, long> { ["t1_pct"] = 55 });
        result.Effective["t1_pct"].Should().Be(55);
        result.Effective["t2_pct"].Should().Be(90, "chưa chỉnh thì theo mặc định hiện hành của phương pháp");
        mine.Params.Should().Be("""{"t1_pct":55}""");
    }

    [Fact]
    public async Task UpdateParams_ANullClearsTheOverride()
    {
        var mine = Active(_cashFlow, UserMethodRole.Primary, """{"t1_pct":55,"t2_pct":85}""");

        var result = await _service.UpdateParamsAsync(_userId, mine.Id, new Dictionary<string, long?> { ["t1_pct"] = null });

        result.Overrides.Should().Equal(new Dictionary<string, long> { ["t2_pct"] = 85 });
        result.Effective["t1_pct"].Should().Be(70);
    }

    [Theory]
    [InlineData("t1_pct", 0)]        // dưới min
    [InlineData("t2_pct", 100)]      // trên max (100% là mức vượt hạn mức cố định)
    [InlineData("ghost", 5)]         // không tồn tại
    [InlineData("other", 5)]         // không cho người dùng sửa
    public async Task UpdateParams_RejectsUnknownLockedOrOutOfRangeValues(string key, long value)
    {
        var mine = Active(_cashFlow, UserMethodRole.Primary);

        var act = () => _service.UpdateParamsAsync(_userId, mine.Id, new Dictionary<string, long?> { [key] = value });

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.MethodParamInvalid);
        _userMethods.Verify(r => r.UpdateAsync(It.IsAny<UserMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(80, 80)]    // T1 = T2
    [InlineData(95, 90)]    // T1 > T2 (T2 mặc định 90)
    public async Task UpdateParams_T1MustStayBelowT2_EvenWhenOnlyOneOfThemIsChanged(long t1, long t2)
    {
        var mine = Active(_cashFlow, UserMethodRole.Primary);

        var both = () => _service.UpdateParamsAsync(_userId, mine.Id, new Dictionary<string, long?> { ["t1_pct"] = t1, ["t2_pct"] = t2 });
        await both.Should().ThrowAsync<BusinessRuleException>();

        var onlyT1 = () => _service.UpdateParamsAsync(_userId, mine.Id, new Dictionary<string, long?> { ["t1_pct"] = 95 });
        await onlyT1.Should().ThrowAsync<BusinessRuleException>("T1 = 95 ≥ T2 mặc định 90");
    }

    [Fact]
    public async Task UpdateParams_OfAnEndedMethodOrSomeoneElses_IsRefused()
    {
        var ended = Active(_cashFlow, UserMethodRole.Primary);
        ended.Status = UserMethodStatus.Ended;

        var act = () => _service.UpdateParamsAsync(_userId, ended.Id, new Dictionary<string, long?> { ["t1_pct"] = 50 });
        await act.Should().ThrowAsync<ConflictException>();

        var other = () => _service.UpdateParamsAsync(_userId, Guid.NewGuid(), new Dictionary<string, long?>());
        await other.Should().ThrowAsync<NotFoundException>();
    }

    // ---- kết thúc và xem ----

    [Fact]
    public async Task End_ClosesTheMethod_AndEndingTwiceIsFine()
    {
        var mine = Active(_cashFlow, UserMethodRole.Primary);

        await _service.EndAsync(_userId, mine.Id);
        await _service.EndAsync(_userId, mine.Id);

        mine.Status.Should().Be(UserMethodStatus.Ended);
        mine.EndedAt.Should().NotBeNull();
        _userMethods.Verify(r => r.UpdateAsync(mine, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMine_ListsThePrimaryFirst_WithTheirMethodDetails()
    {
        Active(_tool, UserMethodRole.Supporting);
        Active(_cashFlow, UserMethodRole.Primary);

        var mine = await _service.GetMineAsync(_userId);

        mine.Select(m => m.Role).Should().Equal("primary", "supporting");
        mine[0].Method.Params.Should().Contain(p => p.Key == "t1_pct");
    }

    [Fact]
    public async Task GetCatalog_OnlyActiveMethods_InDisplayOrder()
    {
        _methods.Setup(r => r.GetMethodsAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Method("b", sortOrder: 2), Method("a", sortOrder: 1)]);

        (await _service.GetCatalogAsync()).Select(m => m.Code).Should().Equal("a", "b");
    }
}
