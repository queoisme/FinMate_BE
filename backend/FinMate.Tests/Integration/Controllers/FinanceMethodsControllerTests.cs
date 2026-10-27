using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Cả luồng quản lý tài chính qua HTTP trên dữ liệu seed thật: khảo sát → gợi ý (không đồng ý nên đi nhánh rule) → chọn →
/// chỉnh T1/T2 → mục tiêu theo phương pháp → tiền khả dụng; cộng quyền admin và cô lập giữa các người dùng.
/// </summary>
[Collection("Integration")]
public class FinanceMethodsControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public FinanceMethodsControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<string> UserTokenAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Password123!", "Finance User"));
        return await LoginAsync(email, "Password123!");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(string? token, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private async Task AnswerEverythingWithFirstOptionAsync(string token)
    {
        var (_, survey) = await SendAsync(token, HttpMethod.Get, "/api/v1/survey");
        var answers = Data(survey).GetProperty("questions").EnumerateArray()
            .Select(q => new { questionCode = q.GetProperty("code").GetString(), optionCode = q.GetProperty("options")[0].GetProperty("code").GetString() })
            .ToList();

        var (status, _) = await SendAsync(token, HttpMethod.Put, "/api/v1/survey/answers", new { answers });
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryEndpoint_RequiresALogin()
    {
        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, "/api/v1/survey"),
            (HttpMethod.Get, "/api/v1/finance-methods"),
            (HttpMethod.Post, "/api/v1/finance-methods/recommendations"),
            (HttpMethod.Get, "/api/v1/me/methods"),
            (HttpMethod.Get, "/api/v1/me/spendable"),
            (HttpMethod.Get, "/api/v1/me/goal-suggestions"),
        })
        {
            (await SendAsync(null, method, url)).Status.Should().Be(HttpStatusCode.Unauthorized, $"{method} {url}");
        }
    }

    [Fact]
    public async Task Catalog_ListsTheSeededMethods_WithEditableThresholds()
    {
        var token = await UserTokenAsync();

        var (status, body) = await SendAsync(token, HttpMethod.Get, "/api/v1/finance-methods");

        status.Should().Be(HttpStatusCode.OK);
        var methods = Data(body).EnumerateArray().ToList();
        methods.Should().NotBeEmpty();
        methods.Select(m => m.GetProperty("code").GetString()).Should().Contain("cash_flow_budget");
        var cashFlow = methods.Single(m => m.GetProperty("code").GetString() == "cash_flow_budget");
        cashFlow.GetProperty("params").EnumerateArray().Select(p => p.GetProperty("key").GetString()).Should().Contain(["t1_pct", "t2_pct"]);
    }

    [Fact]
    public async Task FullFlow_Survey_Recommend_Choose_EditT1T2_Goals_Spendable()
    {
        var token = await UserTokenAsync();

        // Chưa khảo sát thì chưa gợi ý được.
        (await SendAsync(token, HttpMethod.Post, "/api/v1/finance-methods/recommendations")).Status.Should().Be(HttpStatusCode.UnprocessableEntity);

        await AnswerEverythingWithFirstOptionAsync(token);
        var (_, survey) = await SendAsync(token, HttpMethod.Get, "/api/v1/survey");
        Data(survey).GetProperty("status").GetString().Should().Be("completed");

        // Chưa đồng ý dùng AI → chắc chắn là nhánh rule, và có đủ lý do để hiển thị.
        var (recStatus, rec) = await SendAsync(token, HttpMethod.Post, "/api/v1/finance-methods/recommendations");
        recStatus.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        Data(rec).GetProperty("source").GetString().Should().Be("rule_fallback");
        var primaryCode = Data(rec).GetProperty("primary").GetProperty("method").GetProperty("code").GetString()!;
        Data(rec).GetProperty("primary").GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        var recommendationId = Data(rec).GetProperty("id").GetGuid();

        // Chọn đúng cách được gợi ý.
        var (chooseStatus, chosen) = await SendAsync(token, HttpMethod.Post, "/api/v1/me/methods",
            new { methodCode = primaryCode, role = "primary", recommendationId });
        chooseStatus.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        Data(chosen).GetProperty("chosenVia").GetString().Should().Be("recommended");
        var userMethodId = Data(chosen).GetProperty("id").GetGuid();

        // Chỉnh T1/T2: hợp lệ thì lưu; T1 ≥ T2 thì bị chặn. Cách được gợi ý có thể không có T1/T2 cho người dùng sửa —
        // khi đó vẫn phải là 422 rõ ràng, không phải 500.
        var (patchStatus, patched) = await SendAsync(token, HttpMethod.Patch, $"/api/v1/me/methods/{userMethodId}/params",
            new { @params = new Dictionary<string, long?> { ["t1_pct"] = 60, ["t2_pct"] = 85 } });
        if (patchStatus == HttpStatusCode.OK)
        {
            Data(patched).GetProperty("effective").GetProperty("t1_pct").GetInt64().Should().Be(60);
            (await SendAsync(token, HttpMethod.Patch, $"/api/v1/me/methods/{userMethodId}/params",
                new { @params = new Dictionary<string, long?> { ["t1_pct"] = 90 } })).Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        else
        {
            patchStatus.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // Thu nhập + gợi ý mục tiêu + tạo mục tiêu từ mẫu + tiền khả dụng.
        (await SendAsync(token, HttpMethod.Patch, "/api/v1/users/me", new { displayName = "Finance User", monthlyIncomeCents = 20_000_000 })).Status.Should().Be(HttpStatusCode.OK);

        var (_, spendable) = await SendAsync(token, HttpMethod.Get, "/api/v1/me/spendable");
        Data(spendable).GetProperty("incomeCents").GetInt64().Should().Be(20_000_000);
        Data(spendable).GetProperty("spendableCents").GetInt64().Should().BeLessThanOrEqualTo(20_000_000);

        var (_, suggestions) = await SendAsync(token, HttpMethod.Get, "/api/v1/me/goal-suggestions");
        var firstTemplate = Data(suggestions).EnumerateArray().FirstOrDefault();
        if (firstTemplate.ValueKind == JsonValueKind.Object)
        {
            var (goalStatus, _) = await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals", new
            {
                name = "Quỹ test",
                targetCents = 12_000_000,
                deadline = DateTimeOffset.UtcNow.AddDays(120),
                goalTemplateCode = firstTemplate.GetProperty("templateCode").GetString(),
            });
            goalStatus.Should().Be(HttpStatusCode.Created);
        }

        // Kết thúc cách đang dùng: gọi hai lần vẫn ổn, và danh sách rỗng.
        (await SendAsync(token, HttpMethod.Delete, $"/api/v1/me/methods/{userMethodId}")).Status.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await SendAsync(token, HttpMethod.Delete, $"/api/v1/me/methods/{userMethodId}")).Status.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        var (_, mine) = await SendAsync(token, HttpMethod.Get, "/api/v1/me/methods");
        Data(mine).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SurveyAiConsent_IsGrantedAndWithdrawnSeparately_FromTheGeneralOnes()
    {
        var token = await UserTokenAsync();

        var (grantStatus, granted) = await SendAsync(token, HttpMethod.Post, "/api/v1/users/me/consents", new { kind = "surveyAi", version = ConsentVersions.SurveyAi });
        grantStatus.Should().Be(HttpStatusCode.OK);
        Data(granted).GetProperty("surveyAiConsentGranted").GetBoolean().Should().BeTrue();
        Data(granted).GetProperty("trainingConsentGranted").GetBoolean().Should().BeFalse();
        Data(granted).GetProperty("termsAccepted").GetBoolean().Should().BeFalse("đồng ý gửi khảo sát cho AI không thay cho điều khoản chung");

        var (_, withdrawn) = await SendAsync(token, HttpMethod.Delete, "/api/v1/users/me/consents/surveyAi");
        Data(withdrawn).GetProperty("surveyAiConsentGranted").GetBoolean().Should().BeFalse();

        (await SendAsync(token, HttpMethod.Post, "/api/v1/users/me/consents", new { kind = "surveyAi", version = ConsentVersions.SurveyAi + 1 }))
            .Status.Should().Be(HttpStatusCode.Conflict, "app hiển thị bản câu chữ khác bản hiện hành");
    }

    [Fact]
    public async Task SurveyAnswers_AreOnlyVisibleToTheirOwner_AndDeletable()
    {
        var alice = await UserTokenAsync();
        var bob = await UserTokenAsync();
        await AnswerEverythingWithFirstOptionAsync(alice);

        var (_, bobSurvey) = await SendAsync(bob, HttpMethod.Get, "/api/v1/survey");
        Data(bobSurvey).GetProperty("answers").GetArrayLength().Should().Be(0);
        Data(bobSurvey).GetProperty("status").GetString().Should().Be("pending");

        (await SendAsync(alice, HttpMethod.Delete, "/api/v1/survey")).Status.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        var (_, aliceAfter) = await SendAsync(alice, HttpMethod.Get, "/api/v1/survey");
        Data(aliceAfter).GetProperty("answers").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SurveyAnswers_RejectUnknownCodes_AndAnEmptyList()
    {
        var token = await UserTokenAsync();

        (await SendAsync(token, HttpMethod.Put, "/api/v1/survey/answers", new { answers = new[] { new { questionCode = "ghost", optionCode = "A" } } }))
            .Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(token, HttpMethod.Put, "/api/v1/survey/answers", new { answers = Array.Empty<object>() }))
            .Status.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task FeasibilityPreview_NeedsNoSavedGoal()
    {
        var token = await UserTokenAsync();

        var (status, body) = await SendAsync(token, HttpMethod.Post, "/api/v1/saving-goals/feasibility",
            new { targetCents = 12_000_000, savedCents = 0, deadline = DateTimeOffset.UtcNow.AddDays(120) });

        status.Should().Be(HttpStatusCode.OK);
        Data(body).GetProperty("requiredPerMonthCents").GetInt64().Should().BeGreaterThan(0);
    }

    // ---- admin ----

    [Fact]
    public async Task AdminEndpoints_AreForbiddenToOrdinaryUsers_AndOpenToAdmins()
    {
        var user = await UserTokenAsync();
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

        foreach (var url in new[] { "/api/v1/admin/finance-methods", "/api/v1/admin/survey-questions", "/api/v1/admin/goal-templates" })
        {
            (await SendAsync(user, HttpMethod.Get, url)).Status.Should().Be(HttpStatusCode.Forbidden, url);
            (await SendAsync(admin, HttpMethod.Get, url)).Status.Should().Be(HttpStatusCode.OK, url);
        }
    }

    [Fact]
    public async Task Admin_CreatesAMethod_ThenDeactivatesIt_AndItLeavesTheUserCatalog()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var user = await UserTokenAsync();
        var code = $"test_{Guid.NewGuid():N}"[..20];

        var (createStatus, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods", new
        {
            code,
            name = "Phương pháp thử",
            summary = "Tóm tắt",
            role = "primary",
            goalMode = "trackOnly",
            whyText = "Vì thử",
            starterSteps = new[] { "Bước 1", "Bước 2", "Bước 3" },
            eligibility = new { weights = new Dictionary<string, int> { ["income_stable"] = 1 }, excludes = Array.Empty<string>() },
            paramDefs = Array.Empty<object>(),
            sortOrder = 99,
        });
        createStatus.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var id = Data(created).GetProperty("id").GetGuid();

        var (_, catalog) = await SendAsync(user, HttpMethod.Get, "/api/v1/finance-methods");
        Data(catalog).EnumerateArray().Select(m => m.GetProperty("code").GetString()).Should().Contain(code);

        (await SendAsync(admin, HttpMethod.Patch, $"/api/v1/admin/finance-methods/{id}/activation", new { isActive = false })).Status.Should().Be(HttpStatusCode.OK);

        var (_, after) = await SendAsync(user, HttpMethod.Get, "/api/v1/finance-methods");
        Data(after).EnumerateArray().Select(m => m.GetProperty("code").GetString()).Should().NotContain(code, "tắt chứ không xoá — người dùng không còn thấy");

        var (_, adminList) = await SendAsync(admin, HttpMethod.Get, "/api/v1/admin/finance-methods?includeInactive=true");
        Data(adminList).EnumerateArray().Select(m => m.GetProperty("code").GetString()).Should().Contain(code, "admin vẫn thấy cách đã tắt");
    }
}
