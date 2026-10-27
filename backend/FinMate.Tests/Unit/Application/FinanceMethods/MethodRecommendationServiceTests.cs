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

/// <summary>
/// Gợi ý: AI CHỈ khi người dùng đồng ý riêng và AI cho ra kết quả dùng được; mọi trường hợp khác rơi về rule. Backend kiểm lại đầu ra của AI
/// và chỉ gửi cho AI những phương pháp đã qua bộ lọc <c>excludes</c>.
/// </summary>
public class MethodRecommendationServiceTests
{
    private readonly Mock<ISurveyRepository> _surveys = new();
    private readonly Mock<IFinanceMethodRepository> _methods = new();
    private readonly Mock<IUserMethodRepository> _userMethods = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IMethodAiClient> _ai = new();
    private readonly MethodRecommendationService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly User _user;
    private readonly FinanceMethod _main = Method("main", FinanceMethodRole.Primary, new() { ["x"] = 2 }, sortOrder: 1);
    private readonly FinanceMethod _other = Method("other", FinanceMethodRole.Primary, new() { ["x"] = 1 }, sortOrder: 2);
    private readonly FinanceMethod _tool = Method("tool", FinanceMethodRole.Supporting, new() { ["x"] = 2 }, sortOrder: 3);
    private readonly FinanceMethod _blocked = Method("blocked", FinanceMethodRole.Primary, new() { ["x"] = 9 }, ["x"], sortOrder: 0);
    private readonly List<FinanceMethod> _catalog;
    private MethodRecommendation? _saved;

    public MethodRecommendationServiceTests()
    {
        _user = new User { Id = _userId };
        _catalog = [_blocked, _main, _other, _tool];

        var questionId = Guid.NewGuid();
        _surveys.Setup(s => s.GetAnswersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSurveyAnswer { UserId = _userId, QuestionCode = "q", OptionCode = "A" }]);
        _surveys.Setup(s => s.GetQuestionsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SurveyQuestion { Id = questionId, Code = "q" }]);
        _surveys.Setup(s => s.GetOptionsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SurveyOption { QuestionId = questionId, Code = "A", Signals = """{"x":3}""" }]);

        _users.Setup(u => u.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _methods.Setup(m => m.GetMethodsAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync(_catalog);
        _methods.Setup(m => m.GetMethodsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => _catalog.Where(m => ids.Contains(m.Id)).ToList());
        _userMethods.Setup(r => r.AddRecommendationAsync(It.IsAny<MethodRecommendation>(), It.IsAny<CancellationToken>()))
            .Callback<MethodRecommendation, CancellationToken>((r, _) => _saved = r)
            .Returns(Task.CompletedTask);

        _service = new MethodRecommendationService(_surveys.Object, _methods.Object, _userMethods.Object, _users.Object, _ai.Object);
    }

    private void Consent()
    {
        _user.SurveyAiConsentVersion = ConsentVersions.SurveyAi;
        _user.SurveyAiConsentAt = DateTimeOffset.UtcNow;
    }

    private void AiReturns(AiMethodPick? pick)
        => _ai.Setup(a => a.RecommendAsync(It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<AiMethodCandidate>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pick);

    [Fact]
    public async Task WithoutAnyAnswer_Refuses()
    {
        _surveys.Setup(s => s.GetAnswersAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var act = () => _service.GenerateAsync(_userId);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.SurveyNotAnswered);
    }

    [Fact]
    public async Task WithoutTheSurveyAiConsent_NeverCallsTheAi_AndUsesTheRule()
    {
        var result = await _service.GenerateAsync(_userId);

        _ai.Verify(a => a.RecommendAsync(It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<AiMethodCandidate>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Source.Should().Be("rule_fallback");
        result.Primary.Method.Code.Should().Be("main");
        result.Support!.Method.Code.Should().Be("tool");
        result.Primary.Reason.Should().Be("Why main", "nhánh rule dùng why_text của phương pháp");
        _saved!.Source.Should().Be(RecommendationSource.RuleFallback);
        _saved.PromptVersion.Should().BeNull("không gọi AI thì không có prompt");
        _saved.Outcome.Should().Be(RecommendationOutcome.Shown);
    }

    [Fact]
    public async Task AConsentToAnOlderWording_IsNotEnough_UntilTheUserAcceptsTheCurrentOne()
    {
        _user.SurveyAiConsentVersion = ConsentVersions.SurveyAi - 1;   // đã đồng ý bản chỉ nêu Gemini, chưa đồng ý bản nêu thêm OpenRouter
        _user.SurveyAiConsentAt = DateTimeOffset.UtcNow;

        var result = await _service.GenerateAsync(_userId);

        _ai.Verify(a => a.RecommendAsync(It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<AiMethodCandidate>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Source.Should().Be("rule_fallback");
    }

    [Fact]
    public async Task WithConsentAndAValidAiPick_UsesTheAi_AndStoresThePromptVersionAndReasons()
    {
        Consent();
        AiReturns(new AiMethodPick("other", "AI chọn other", "tool", "AI chọn tool", "method-select-v1"));

        var result = await _service.GenerateAsync(_userId);

        result.Source.Should().Be("ai");
        result.Primary.Method.Code.Should().Be("other", "AI được phép chọn khác rule miễn là nằm trong danh sách");
        result.Primary.Reason.Should().Be("AI chọn other");
        result.Support!.Reason.Should().Be("AI chọn tool");
        _saved!.Source.Should().Be(RecommendationSource.Ai);
        _saved.PromptVersion.Should().Be("method-select-v1");
    }

    [Fact]
    public async Task TheAiIsOnlyGivenMethodsThatPassedTheExcludesFilter_AndNoUserIdentity()
    {
        Consent();
        IReadOnlyList<AiMethodCandidate>? sent = null;
        IReadOnlyDictionary<string, int>? sentSignals = null;
        _ai.Setup(a => a.RecommendAsync(It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<AiMethodCandidate>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<string, int>, IReadOnlyList<AiMethodCandidate>, CancellationToken>((s, m, _) => { sentSignals = s; sent = m; })
            .ReturnsAsync((AiMethodPick?)null);

        await _service.GenerateAsync(_userId);

        sent.Should().NotBeNull();
        sent!.Select(m => m.Code).Should().BeEquivalentTo("main", "other", "tool");
        sent!.Select(m => m.Code).Should().NotContain("blocked", "phương pháp bị signal của người dùng loại thì không được đưa cho AI chọn");
        sentSignals.Should().Equal(new Dictionary<string, int> { ["x"] = 3 });
    }

    [Fact]
    public async Task AnAiPickOutsideTheShortlist_IsRejected_AndTheRuleTakesOver()
    {
        Consent();
        AiReturns(new AiMethodPick("blocked", "AI cố chọn cái bị loại", null, null, "v1"));

        var result = await _service.GenerateAsync(_userId);

        result.Source.Should().Be("rule_fallback");
        result.Primary.Method.Code.Should().Be("main");
        _saved!.PromptVersion.Should().BeNull();
    }

    [Theory]
    [InlineData("tool", "ok", null, null)]            // cách chỉ-bổ-trợ làm cách chính
    [InlineData("main", "ok", "main", "ok")]          // bổ trợ trùng chính
    [InlineData("main", "", null, null)]              // lý do rỗng
    public async Task AnAiPickThatBreaksARule_FallsBackToTheRule(string primary, string reason, string? support, string? supportReason)
    {
        Consent();
        AiReturns(new AiMethodPick(primary, reason, support, supportReason, "v1"));

        (await _service.GenerateAsync(_userId)).Source.Should().Be("rule_fallback");
    }

    [Fact]
    public async Task WhenTheAiGivesNothing_TheRuleTakesOver()
    {
        Consent();
        AiReturns(null);

        var result = await _service.GenerateAsync(_userId);

        result.Source.Should().Be("rule_fallback");
        result.Primary.Method.Code.Should().Be("main");
    }

    [Fact]
    public async Task LongMethodTextIsTruncatedToTheAiContractLimits()
    {
        Consent();
        _main.Name = new string('n', 150);
        _main.Summary = new string('s', 500);
        IReadOnlyList<AiMethodCandidate>? sent = null;
        _ai.Setup(a => a.RecommendAsync(It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<AiMethodCandidate>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyDictionary<string, int>, IReadOnlyList<AiMethodCandidate>, CancellationToken>((_, m, _) => sent = m)
            .ReturnsAsync((AiMethodPick?)null);

        await _service.GenerateAsync(_userId);

        var candidate = sent!.Single(m => m.Code == "main");
        candidate.Name.Length.Should().Be(100);
        candidate.Summary.Length.Should().Be(400);
    }

    [Fact]
    public async Task AskingAgainWithTheSameSignalsWithinTenMinutes_ReturnsTheLatestWithoutCallingTheAiOrSavingAgain()
    {
        Consent();
        _userMethods.Setup(r => r.GetLatestRecommendationAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(new MethodRecommendation
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            PrimaryMethodId = _main.Id,
            Reasons = """[{"role":"primary","code":"main","reason":"Lý do đã lưu"}]""",
            SignalsSnapshot = """{ "x": 3 }""",
            Source = RecommendationSource.RuleFallback,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
        });

        var result = await _service.GenerateAsync(_userId);

        result.Primary.Reason.Should().Be("Lý do đã lưu");
        _saved.Should().BeNull();
        _ai.Verify(a => a.RecommendAsync(It.IsAny<IReadOnlyDictionary<string, int>>(), It.IsAny<IReadOnlyList<AiMethodCandidate>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-30, """{"x":3}""")]    // quá 10 phút
    [InlineData(-1, """{"x":2}""")]     // signals khác
    public async Task AnOldOrDifferentRecommendation_IsNotReused(int minutesAgo, string snapshot)
    {
        _userMethods.Setup(r => r.GetLatestRecommendationAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(new MethodRecommendation
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            PrimaryMethodId = _main.Id,
            Reasons = "[]",
            SignalsSnapshot = snapshot,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(minutesAgo),
        });

        await _service.GenerateAsync(_userId);

        _saved.Should().NotBeNull();
    }

    [Fact]
    public async Task WhenNoPrimaryCapableMethodSurvivesTheFilter_Refuses()
    {
        _catalog.Clear();
        _catalog.Add(_tool);

        var act = () => _service.GenerateAsync(_userId);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.NoMethodAvailable);
    }

    [Fact]
    public async Task TheSavedRecommendationRecordsTheSignalsAndTheMethodVersionsOfTheShortlist()
    {
        _main.Version = 4;

        await _service.GenerateAsync(_userId);

        _saved!.SignalsSnapshot.Should().Contain("\"x\":3");
        _saved.MethodVersions.Should().Contain("\"main\":4").And.NotContain("blocked");
    }

    // ---- bỏ qua ----

    [Fact]
    public async Task Skip_MarksTheRecommendationSkipped_AndASecondSkipConflicts()
    {
        var recommendation = new MethodRecommendation { Id = Guid.NewGuid(), UserId = _userId, PrimaryMethodId = _main.Id, Reasons = "[]" };
        _userMethods.Setup(r => r.GetRecommendationAsync(recommendation.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(recommendation);

        var result = await _service.SkipAsync(_userId, recommendation.Id);

        result.Outcome.Should().Be("skipped");
        recommendation.DecidedAt.Should().NotBeNull();

        var again = () => _service.SkipAsync(_userId, recommendation.Id);

        (await again.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(FinanceErrorCodes.RecommendationAlreadyDecided);
    }

    [Fact]
    public async Task Skip_OfSomeoneElsesOrUnknownRecommendation_IsNotFound()
    {
        var act = () => _service.SkipAsync(_userId, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetLatest_WithNone_IsNotFound()
    {
        var act = () => _service.GetLatestAsync(_userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
