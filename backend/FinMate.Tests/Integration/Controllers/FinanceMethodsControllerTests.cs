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

    // ---- khối "hũ": chia chi tiêu tháng này theo danh mục thuộc từng hũ ----

    private static object[] JarsLayout(params object[] jars) => [new { type = "jars", title = "Các hũ", jars }];

    private async Task<(Guid Id, string Slug)[]> SystemCategoriesAsync(string token, int count)
    {
        var (_, list) = await SendAsync(token, HttpMethod.Get, "/api/v1/categories");
        return Data(list).EnumerateArray()
            .Where(c => c.GetProperty("isSystem").GetBoolean() && c.GetProperty("slug").GetString() != "loan")
            .Take(count)
            .Select(c => (c.GetProperty("id").GetGuid(), c.GetProperty("slug").GetString()!))
            .ToArray();
    }

    private async Task<Guid> CashAccountAsync(string token, long balance = 50_000_000)
    {
        var (_, account) = await SendAsync(token, HttpMethod.Post, "/api/v1/financial-accounts", new { accountType = "Cash", accountName = "Ví", initialBalanceCents = balance });
        return Data(account).GetProperty("id").GetGuid();
    }

    private Task SpendAsync(string token, Guid account, Guid category, long amount, string type = "Debit")
        => SendAsync(token, HttpMethod.Post, "/api/v1/transactions", new
        {
            financialAccountId = account,
            categoryId = category,
            amountCents = amount,
            transactionType = type,
            transactedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            merchantName = "Test",
        });

    [Fact]
    public async Task Jars_SplitThisMonthsSpendingByCategory_AgainstTheShareOfEachJar()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var user = await UserTokenAsync();
        var categories = await SystemCategoriesAsync(user, 3);
        var layout = JarsLayout(
            new { key = "jar_a", label = "Thiết yếu", categories = new[] { categories[0].Slug } },
            new { key = "jar_b", categories = new[] { categories[1].Slug } });
        var (createStatus, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods", NewMethod(NewCode(), new[] { Param("jar_a", def: 60), Param("jar_b", def: 40) }, layout));
        createStatus.Should().Be(HttpStatusCode.Created);

        await SendAsync(user, HttpMethod.Patch, "/api/v1/users/me", new { displayName = "Jar User", monthlyIncomeCents = 10_000_000L });
        var (_, chosen) = await SendAsync(user, HttpMethod.Post, "/api/v1/me/methods", new { methodCode = Data(created).GetProperty("code").GetString() });
        var userMethodId = Data(chosen).GetProperty("id").GetGuid();
        var account = await CashAccountAsync(user);
        await SpendAsync(user, account, categories[0].Id, 1_500_000);
        await SpendAsync(user, account, categories[1].Id, 3_700_000);
        await SpendAsync(user, account, categories[2].Id, 500_000);                        // ngoài mọi hũ
        await SpendAsync(user, account, categories[0].Id, 2_000_000, type: "Credit");      // thu nhập không phải chi tiêu

        async Task<JsonElement> JarSetAsync()
        {
            var (status, body) = await SendAsync(user, HttpMethod.Get, "/api/v1/me/jars");
            status.Should().Be(HttpStatusCode.OK);
            return Data(body).EnumerateArray().Single();
        }

        var set = await JarSetAsync();
        set.GetProperty("userMethodId").GetGuid().Should().Be(userMethodId);
        set.GetProperty("incomeCents").GetInt64().Should().Be(10_000_000);
        (set.GetProperty("t1Pct").GetInt32(), set.GetProperty("t2Pct").GetInt32()).Should().Be((70, 90));
        set.GetProperty("unassignedSpentCents").GetInt64().Should().Be(500_000);

        var a = set.GetProperty("jars")[0];
        a.GetProperty("label").GetString().Should().Be("Thiết yếu");
        (a.GetProperty("allocationPct").GetInt32(), a.GetProperty("allocatedCents").GetInt64(), a.GetProperty("spentCents").GetInt64()).Should().Be((60, 6_000_000, 1_500_000));
        (a.GetProperty("remainingCents").GetInt64(), a.GetProperty("usedPct").GetInt32(), a.GetProperty("level").GetString()).Should().Be((4_500_000, 25, "ok"));
        a.GetProperty("categories")[0].GetProperty("slug").GetString().Should().Be(categories[0].Slug);

        var b = set.GetProperty("jars")[1];
        b.GetProperty("label").GetString().Should().Be("jar_b", "không đặt tên thì lấy nhãn của tham số");
        (b.GetProperty("allocatedCents").GetInt64(), b.GetProperty("spentCents").GetInt64(), b.GetProperty("usedPct").GetInt32(), b.GetProperty("level").GetString())
            .Should().Be((4_000_000, 3_700_000, 92, "t2"));

        // Người dùng đổi tỷ lệ của hũ: số được chia và mức cảnh báo tính lại ngay.
        await SendAsync(user, HttpMethod.Patch, $"/api/v1/me/methods/{userMethodId}/params", new { @params = new Dictionary<string, int> { ["jar_a"] = 50 } });
        var after = (await JarSetAsync()).GetProperty("jars")[0];
        (after.GetProperty("allocationPct").GetInt32(), after.GetProperty("allocatedCents").GetInt64(), after.GetProperty("usedPct").GetInt32()).Should().Be((50, 5_000_000, 30));
    }

    [Fact]
    public async Task Jars_WithoutIncome_ShowSpendingButNeverInventAnAllocation()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var user = await UserTokenAsync();
        var categories = await SystemCategoriesAsync(user, 1);
        var (_, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods",
            NewMethod(NewCode(), new[] { Param("jar_a", def: 100) }, JarsLayout(new { key = "jar_a", categories = new[] { categories[0].Slug } })));
        await SendAsync(user, HttpMethod.Post, "/api/v1/me/methods", new { methodCode = Data(created).GetProperty("code").GetString() });
        await SpendAsync(user, await CashAccountAsync(user), categories[0].Id, 800_000);

        var (_, body) = await SendAsync(user, HttpMethod.Get, "/api/v1/me/jars");

        var set = Data(body).EnumerateArray().Single();
        set.GetProperty("incomeCents").ValueKind.Should().Be(JsonValueKind.Null);
        var jar = set.GetProperty("jars")[0];
        jar.GetProperty("spentCents").GetInt64().Should().Be(800_000);
        jar.GetProperty("allocatedCents").ValueKind.Should().Be(JsonValueKind.Null);
        jar.GetProperty("remainingCents").ValueKind.Should().Be(JsonValueKind.Null);
        jar.GetProperty("level").GetString().Should().Be("none");
    }

    [Fact]
    public async Task Jars_AreEmpty_WhenNoMethodInUseHasAJarsBlock_AndNeedALogin()
    {
        var user = await UserTokenAsync();
        await SendAsync(user, HttpMethod.Post, "/api/v1/me/methods", new { methodCode = "category_budget" });   // bố cục không có khối hũ

        var (status, body) = await SendAsync(user, HttpMethod.Get, "/api/v1/me/jars");

        status.Should().Be(HttpStatusCode.OK);
        Data(body).GetArrayLength().Should().Be(0);
        (await SendAsync(null, HttpMethod.Get, "/api/v1/me/jars")).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    public static IEnumerable<object[]> InvalidJarLayouts()
    {
        yield return [new object[] { new { type = "jars" } }, "homeLayout[0].jars"];                                                                                    // thiếu danh sách hũ
        yield return [JarsLayout(new { key = "minutes_x", categories = new[] { "food" } }), "homeLayout[0].jars[0].key"];                                               // không phải tham số %
        yield return [JarsLayout(new { key = "khong_co", categories = new[] { "food" } }), "homeLayout[0].jars[0].key"];                                                // tham số không tồn tại
        yield return [JarsLayout(new { key = "jar_a", categories = new[] { "food" } }, new { key = "jar_a", categories = new string[0] }), "homeLayout[0].jars[1].key"]; // một tham số hai hũ
        yield return [JarsLayout(new { key = "jar_a", categories = new[] { "food" } }, new { key = "jar_b", categories = new[] { "food" } }), "homeLayout[0].jars[1].categories"]; // một danh mục hai hũ
        yield return [JarsLayout(new { key = "jar_a", categories = new[] { "danh-muc-khong-co" } }), "homeLayout"];                                                      // danh mục không có thật
        yield return [JarsLayout(new { key = "jar_a", categories = new[] { "Food Spaces" } }), "homeLayout[0].jars[0].categories"];                                       // sai định dạng mã
        yield return [JarsLayout(new { key = "jar_a", categories = new string[0] }, new { key = "jar_b", categories = new string[0] }, new { key = "jar_c", categories = new string[0] }), "homeLayout[0].jars"]; // tổng 130%
        yield return [new object[] { new { type = "params", jars = new object[0] } }, "homeLayout[0]"];                                                                 // jars sai chỗ
        yield return [new object[] { new { type = "jars", keys = new[] { "jar_a" }, jars = new object[] { new { key = "jar_a", categories = new string[0] } } } }, "homeLayout[0]"]; // keys sai chỗ
        yield return [JarsLayout(new { key = "jar_a", label = new string('x', 41), categories = new string[0] }), "homeLayout[0].jars[0].label"];                       // tên hũ quá dài
        yield return [JarsLayout(new { key = "jar_a", categories = new string[0], thua = 1 }), "homeLayout[0].jars[0]"];                                                 // trường lạ
    }

    [Theory]
    [MemberData(nameof(InvalidJarLayouts))]
    public async Task InvalidJarLayouts_AreRefusedWithTheFieldThatIsWrong(object[] layout, string expectedField)
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

        var (status, body) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods",
            NewMethod(NewCode(), new[] { Param("jar_a", def: 60), Param("jar_b", def: 40), Param("jar_c", def: 30), Param("minutes_x", "minutes", 60) }, layout));

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString()).Should().Contain(expectedField);
    }

    [Fact]
    public async Task AJar_CannotBeBoundToAnAlertThreshold_EvenThoughThatParamIsAlsoAPercentage()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var paramDefs = new[] { Param("t1_pct", def: 70), Param("t2_pct", def: 90), Param("jar_a", def: 50) };

        var (status, body) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods",
            NewMethod(NewCode(), paramDefs, JarsLayout(new { key = "t1_pct", categories = new string[0] })));

        status.Should().Be(HttpStatusCode.BadRequest);
        var details = body.GetProperty("error").GetProperty("details").EnumerateArray().ToList();
        details.Should().Contain(d => d.GetProperty("field").GetString() == "homeLayout[0].jars[0].key" && d.GetProperty("message").GetString()!.Contains("T1/T2"));
    }

    // ---- admin tắt một cách đang có người dùng: báo và mời chọn lại, không gỡ của ai ----

    [Fact]
    public async Task DisablingAMethodInUse_KeepsItForItsUsers_FlagsItUnavailable_AndShowsAdminHowManyAreAffected()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var user = await UserTokenAsync();
        var newcomer = await UserTokenAsync();
        var code = NewCode();
        var (_, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods", NewMethod(code, Array.Empty<object>(), null));
        var methodId = Data(created).GetProperty("id").GetGuid();
        var (_, chosen) = await SendAsync(user, HttpMethod.Post, "/api/v1/me/methods", new { methodCode = code });
        var userMethodId = Data(chosen).GetProperty("id").GetGuid();
        Data(chosen).GetProperty("method").GetProperty("available").GetBoolean().Should().BeTrue();

        async Task<JsonElement> AdminRowAsync()
        {
            var (_, list) = await SendAsync(admin, HttpMethod.Get, "/api/v1/admin/finance-methods?includeInactive=true");
            return Data(list).EnumerateArray().Single(m => m.GetProperty("code").GetString() == code);
        }

        (await AdminRowAsync()).GetProperty("activeUserCount").GetInt32().Should().Be(1);

        var (offStatus, off) = await SendAsync(admin, HttpMethod.Patch, $"/api/v1/admin/finance-methods/{methodId}/activation", new { isActive = false });
        offStatus.Should().Be(HttpStatusCode.OK);
        Data(off).GetProperty("activeUserCount").GetInt32().Should().Be(1, "admin thấy ngay có bao nhiêu người bị ảnh hưởng");

        var (_, mine) = await SendAsync(user, HttpMethod.Get, "/api/v1/me/methods");
        var kept = Data(mine).EnumerateArray().Single(m => m.GetProperty("method").GetProperty("code").GetString() == code);
        kept.GetProperty("method").GetProperty("available").GetBoolean().Should().BeFalse("app dựa vào cờ này để báo và mời chọn cách khác");

        var (blockedStatus, blocked) = await SendAsync(newcomer, HttpMethod.Post, "/api/v1/me/methods", new { methodCode = code });
        blockedStatus.Should().Be(HttpStatusCode.UnprocessableEntity);
        blocked.GetProperty("error").GetProperty("code").GetString().Should().Be("FINANCE_METHOD_INACTIVE");
        var (_, catalog) = await SendAsync(newcomer, HttpMethod.Get, "/api/v1/finance-methods");
        Data(catalog).EnumerateArray().Select(m => m.GetProperty("code").GetString()).Should().NotContain(code);

        (await SendAsync(user, HttpMethod.Delete, $"/api/v1/me/methods/{userMethodId}")).Status.Should().Be(HttpStatusCode.NoContent);
        (await AdminRowAsync()).GetProperty("activeUserCount").GetInt32().Should().Be(0, "người dùng tự ngừng thì không còn ai bị ảnh hưởng");
    }

    // ---- ngưỡng cảnh báo: cách quản lý → cá nhân → mặc định hệ thống ----

    private async Task<JsonElement> AlertThresholdsAsync(string token)
    {
        var (status, body) = await SendAsync(token, HttpMethod.Get, "/api/v1/me/alert-thresholds");
        status.Should().Be(HttpStatusCode.OK);
        return Data(body);
    }

    [Fact]
    public async Task AlertThresholds_WithoutAMethod_UseTheSystemDefault_AndAPersonalOneCanBeSetAndCleared()
    {
        var token = await UserTokenAsync();

        var initial = await AlertThresholdsAsync(token);
        initial.GetProperty("source").GetString().Should().Be("system");
        (initial.GetProperty("t1Pct").GetInt32(), initial.GetProperty("t2Pct").GetInt32()).Should().Be((70, 90));
        initial.GetProperty("personal").ValueKind.Should().Be(JsonValueKind.Null);

        var (setStatus, set) = await SendAsync(token, HttpMethod.Put, "/api/v1/me/alert-thresholds", new { t1Pct = 50, t2Pct = 80 });
        setStatus.Should().Be(HttpStatusCode.OK);
        Data(set).GetProperty("source").GetString().Should().Be("personal");
        (Data(set).GetProperty("t1Pct").GetInt32(), Data(set).GetProperty("t2Pct").GetInt32()).Should().Be((50, 80));
        Data(set).GetProperty("personal").GetProperty("t1Pct").GetInt32().Should().Be(50);
        Data(set).GetProperty("systemDefault").GetProperty("t2Pct").GetInt32().Should().Be(90);

        var (clearStatus, cleared) = await SendAsync(token, HttpMethod.Delete, "/api/v1/me/alert-thresholds");
        clearStatus.Should().Be(HttpStatusCode.OK);
        Data(cleared).GetProperty("source").GetString().Should().Be("system");
        Data(cleared).GetProperty("personal").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(90, 60)]    // T1 phải nhỏ hơn T2
    [InlineData(50, 50)]
    [InlineData(50, 100)]   // 100% là mức "vượt hạn mức" cố định
    [InlineData(0, 50)]
    public async Task AlertThresholds_InvalidPersonalValues_AreRefused_AndNothingIsSaved(int t1, int t2)
    {
        var token = await UserTokenAsync();

        var (status, body) = await SendAsync(token, HttpMethod.Put, "/api/v1/me/alert-thresholds", new { t1Pct = t1, t2Pct = t2 });

        status.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("ALERT_THRESHOLD_INVALID");
        (await AlertThresholdsAsync(token)).GetProperty("source").GetString().Should().Be("system");
    }

    [Fact]
    public async Task AlertThresholds_AMethodWithThresholds_TakesOver_AndEndingItGivesThePersonalOneBack()
    {
        var token = await UserTokenAsync();
        await SendAsync(token, HttpMethod.Put, "/api/v1/me/alert-thresholds", new { t1Pct = 50, t2Pct = 80 });

        var (_, chosen) = await SendAsync(token, HttpMethod.Post, "/api/v1/me/methods", new { methodCode = "category_budget" });
        var userMethodId = Data(chosen).GetProperty("id").GetGuid();
        var withMethod = await AlertThresholdsAsync(token);

        withMethod.GetProperty("source").GetString().Should().Be("method");
        withMethod.GetProperty("methodCode").GetString().Should().Be("category_budget");
        (withMethod.GetProperty("t1Pct").GetInt32(), withMethod.GetProperty("t2Pct").GetInt32()).Should().Be((70, 90), "theo mặc định của cách quản lý, không phải 50/80 cá nhân");
        withMethod.GetProperty("personal").GetProperty("t1Pct").GetInt32().Should().Be(50, "ngưỡng cá nhân vẫn được giữ, chỉ bị cách quản lý che");

        (await SendAsync(token, HttpMethod.Delete, $"/api/v1/me/methods/{userMethodId}")).Status.Should().Be(HttpStatusCode.NoContent);
        var afterEnding = await AlertThresholdsAsync(token);
        afterEnding.GetProperty("source").GetString().Should().Be("personal");
        afterEnding.GetProperty("t1Pct").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task AlertThresholds_RequireALogin()
    {
        (await SendAsync(null, HttpMethod.Get, "/api/v1/me/alert-thresholds")).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(null, HttpMethod.Put, "/api/v1/me/alert-thresholds", new { t1Pct = 50, t2Pct = 80 })).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(null, HttpMethod.Delete, "/api/v1/me/alert-thresholds")).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- bố cục trang chủ (home_layout) ----

    private static object NewMethod(string code, object paramDefs, object? homeLayout) => new
    {
        code,
        name = "Phương pháp thử",
        summary = "Tóm tắt",
        role = "primary",
        goalMode = "trackOnly",
        whyText = "Vì thử",
        starterSteps = new[] { "Bước 1" },
        eligibility = new { weights = new Dictionary<string, int> { ["income_stable"] = 1 }, excludes = Array.Empty<string>() },
        paramDefs,
        homeLayout,
        sortOrder = 98,
    };

    private static object Param(string key, string type = "percent", int def = 10) => new { key, label = key, type, @default = def, min = 0, max = 100, userEditable = true };

    private static string NewCode() => $"layout_{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task EveryMethodInTheUserCatalog_CarriesAHomeLayout_AndTrackingOnlyMethodsSkipSpendable()
    {
        var user = await UserTokenAsync();

        var (_, catalog) = await SendAsync(user, HttpMethod.Get, "/api/v1/finance-methods");

        var methods = Data(catalog).EnumerateArray().ToList();
        methods.Should().OnlyContain(m => m.GetProperty("homeLayout").GetArrayLength() > 0);
        string[] TypesOf(string code) => methods.Single(m => m.GetProperty("code").GetString() == code)
            .GetProperty("homeLayout").EnumerateArray().Select(b => b.GetProperty("type").GetString()!).ToArray();
        TypesOf("minimal_tracking").Should().NotContain("spendable");
        TypesOf("category_budget").Should().Equal("spendable", "params", "params", "goals");
        var allocation = methods.Single(m => m.GetProperty("code").GetString() == "category_budget")
            .GetProperty("homeLayout").EnumerateArray().Single(b => b.TryGetProperty("display", out var d) && d.GetString() == "allocation");
        allocation.GetProperty("keys").EnumerateArray().Select(k => k.GetString()).Should().Equal("needs_pct", "wants_pct", "goals_pct");
    }

    [Fact]
    public async Task HomeBlockTypes_AreListedForAdminsOnly()
    {
        var user = await UserTokenAsync();
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

        (await SendAsync(user, HttpMethod.Get, "/api/v1/admin/finance-methods/home-blocks")).Status.Should().Be(HttpStatusCode.Forbidden);
        var (status, body) = await SendAsync(admin, HttpMethod.Get, "/api/v1/admin/finance-methods/home-blocks");

        status.Should().Be(HttpStatusCode.OK);
        Data(body).EnumerateArray().Select(t => t.GetProperty("type").GetString()).Should().Equal("spendable", "params", "jars", "goals", "starter_steps");
        Data(body).EnumerateArray().Single(t => t.GetProperty("type").GetString() == "jars").GetProperty("acceptsJars").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Admin_SetsACustomLayout_UsersSeeIt_AndEditingItBumpsTheVersion()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var user = await UserTokenAsync();
        var code = NewCode();
        var layout = new object[]
        {
            new { type = "params", title = "Sáu hũ", keys = new[] { "jar_a", "jar_b" }, display = "allocation" },
            new { type = "starter_steps" },
        };

        var (createStatus, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods", NewMethod(code, new[] { Param("jar_a"), Param("jar_b") }, layout));

        createStatus.Should().Be(HttpStatusCode.Created);
        var id = Data(created).GetProperty("id").GetGuid();
        var (_, catalog) = await SendAsync(user, HttpMethod.Get, "/api/v1/finance-methods");
        var mine = Data(catalog).EnumerateArray().Single(m => m.GetProperty("code").GetString() == code);
        mine.GetProperty("homeLayout")[0].GetProperty("title").GetString().Should().Be("Sáu hũ");

        var (patchStatus, patched) = await SendAsync(admin, HttpMethod.Patch, $"/api/v1/admin/finance-methods/{id}", new { homeLayout = new object[] { new { type = "goals" } } });

        patchStatus.Should().Be(HttpStatusCode.OK);
        Data(patched).GetProperty("version").GetInt32().Should().Be(2);
        Data(patched).GetProperty("homeLayout").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task OmittingTheLayout_UsesTheDefaultOne()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

        var (_, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods", NewMethod(NewCode(), Array.Empty<object>(), null));

        Data(created).GetProperty("homeLayout").EnumerateArray().Select(b => b.GetProperty("type").GetString()).Should().Equal("spendable", "params", "goals", "starter_steps");
    }

    public static IEnumerable<object[]> InvalidLayouts()
    {
        yield return [new object[0], "homeLayout"];                                                                        // rỗng
        yield return [new object[] { new { type = "chart" } }, "homeLayout[0].type"];                                      // loại không biết
        yield return [new object[] { new { type = "spendable" }, new { type = "spendable" } }, "homeLayout[1].type"];     // lặp khối chỉ-một-lần
        yield return [new object[] { new { type = "params", keys = new[] { "khong_co" } } }, "homeLayout[0].keys"];       // khóa không có trong paramDefs
        yield return [new object[] { new { type = "params", keys = new[] { "jar_a", "jar_a" } } }, "homeLayout[0].keys"]; // khóa trùng
        yield return [new object[] { new { type = "params", keys = new[] { "minutes_x" }, display = "allocation" } }, "homeLayout[0].display"]; // allocation trên tham số không phải percent
        yield return [new object[] { new { type = "goals", keys = new[] { "jar_a" } } }, "homeLayout[0]"];               // keys sai chỗ
        yield return [new object[] { new { type = "starter_steps", title = new string('x', 61) } }, "homeLayout[0].title"]; // tiêu đề quá dài
        yield return [Enumerable.Repeat<object>(new { type = "params" }, 9).ToArray(), "homeLayout"];                    // quá 8 khối
        yield return [new object[] { new { type = "params", extra = 1 } }, "homeLayout[0]"];                              // trường lạ
    }

    [Theory]
    [MemberData(nameof(InvalidLayouts))]
    public async Task InvalidLayouts_AreRefusedWithTheFieldThatIsWrong(object[] layout, string expectedField)
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);

        var (status, body) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods",
            NewMethod(NewCode(), new[] { Param("jar_a"), Param("minutes_x", "minutes", 60) }, layout));

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString()).Should().Contain(expectedField);
    }

    [Fact]
    public async Task RemovingAParamTheLayoutPointsAt_IsRefused_InsteadOfBreakingEveryUsersHome()
    {
        var admin = await LoginAsync(AuthApiFactory.AdminEmail, AuthApiFactory.AdminPassword);
        var layout = new object[] { new { type = "params", keys = new[] { "jar_a" }, display = "allocation" } };
        var (_, created) = await SendAsync(admin, HttpMethod.Post, "/api/v1/admin/finance-methods", NewMethod(NewCode(), new[] { Param("jar_a") }, layout));
        var id = Data(created).GetProperty("id").GetGuid();

        var (status, body) = await SendAsync(admin, HttpMethod.Patch, $"/api/v1/admin/finance-methods/{id}", new { paramDefs = new[] { Param("jar_b") } });

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString()).Should().Contain("homeLayout[0].keys");
    }
}
