using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class CategoriesControllerTests : IClassFixture<AuthApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public CategoriesControllerTests(AuthApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private record AuthResultBody(bool Success, AuthResultData Data);
    private record AuthResultData(string AccessToken);
    private record CategoryBody(bool Success, CategoryData Data);
    private record CategoryData(Guid Id, string Name, string Slug, string? IconName, bool IsSystem);
    private record CategoryListBody(bool Success, List<CategoryData> Data);

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@finmate.local";
        const string password = "Password123!";

        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(email, password, "Category Test User"));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResultBody>(JsonOptions);
        return loginBody!.Data.AccessToken;
    }

    private HttpRequestMessage AuthedRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    [Fact]
    public async Task GetList_IncludesAllTwelveSystemCategories()
    {
        var accessToken = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);

        body!.Data.Count(c => c.IsSystem).Should().Be(12);  // 11 danh mục chi/thu + "loan" (Cho vay / Mượn)
        body.Data.Select(c => c.Slug).Should().Contain(new[] { "food", "income", "loan", "other" });
    }

    private record FullCategoryData(
        Guid Id, string Name, string Slug, string? IconName, bool IsSystem, string? Color, string Kind, Guid? ParentCategoryId, long? SuggestedBudgetCents, bool IsFavorite);
    private record FullCategoryBody(bool Success, FullCategoryData Data);
    private record FullCategoryListBody(bool Success, List<FullCategoryData> Data);
    private record ColorData(string Key, string Hex, string Name);
    private record OptionsData(List<ColorData> Palette, List<string> Kinds);
    private record OptionsBody(bool Success, OptionsData Data);

    private async Task<Guid> SystemCategoryIdAsync(string accessToken, string slug)
    {
        var list = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        var body = await list.Content.ReadFromJsonAsync<FullCategoryListBody>(JsonOptions);
        return body!.Data.Single(c => c.IsSystem && c.Slug == slug).Id;
    }

    [Fact]
    public async Task Options_ReturnsThePaletteFromSystemConfigAndTheKinds()
    {
        var accessToken = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories/options", accessToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OptionsBody>(JsonOptions);
        body!.Data.Palette.Should().HaveCount(8);
        body.Data.Palette.Should().Contain(c => c.Key == "honey" && c.Hex == "#F0C8A0" && c.Name == "Cam mật ong");
        body.Data.Kinds.Should().Equal("expense", "income");
    }

    [Fact]
    public async Task Options_RequiresAuthentication()
    {
        var response = await _client.GetAsync("/api/v1/categories/options");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SystemIncomeCategoryIsOfKindIncomeAndTheRestAreExpense()
    {
        var accessToken = await RegisterAndLoginAsync();

        var list = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        var body = await list.Content.ReadFromJsonAsync<FullCategoryListBody>(JsonOptions);

        body!.Data.Single(c => c.IsSystem && c.Slug == "income").Kind.Should().Be("income");
        body.Data.Where(c => c.IsSystem && c.Slug != "income").Should().OnlyContain(c => c.Kind == "expense");
    }

    [Fact]
    public async Task CreateWithAppearance_IsReturnedByTheListAndCanBeEditedAndCleared()
    {
        var accessToken = await RegisterAndLoginAsync();
        var foodId = await SystemCategoryIdAsync(accessToken, "food");

        var create = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        create.Content = JsonContent.Create(new
        {
            name = "Cà phê & Gặp gỡ", iconName = "coffee", color = "#f0c8a0", kind = "expense", parentCategoryId = foodId, suggestedBudgetCents = 1_500_000, isFavorite = true,
        });
        var created = await _client.SendAsync(create);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = (await created.Content.ReadFromJsonAsync<FullCategoryBody>(JsonOptions))!.Data;
        createdBody.Color.Should().Be("#F0C8A0");
        createdBody.ParentCategoryId.Should().Be(foodId);
        createdBody.SuggestedBudgetCents.Should().Be(1_500_000);
        createdBody.IsFavorite.Should().BeTrue();

        var list = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        var listed = (await list.Content.ReadFromJsonAsync<FullCategoryListBody>(JsonOptions))!.Data.Single(c => c.Id == createdBody.Id);
        listed.Should().BeEquivalentTo(createdBody);

        // Gửi chỉ tên và icon (app cũ): màu, nhóm, hạn mức, yêu thích giữ nguyên.
        var rename = AuthedRequest(HttpMethod.Patch, $"/api/v1/categories/{createdBody.Id}", accessToken);
        rename.Content = JsonContent.Create(new { name = "Cà phê", iconName = "coffee" });
        var renamed = (await (await _client.SendAsync(rename)).Content.ReadFromJsonAsync<FullCategoryBody>(JsonOptions))!.Data;
        renamed.Color.Should().Be("#F0C8A0");
        renamed.ParentCategoryId.Should().Be(foodId);
        renamed.IsFavorite.Should().BeTrue();

        // Bỏ nhóm cha và hạn mức, đổi màu, tắt yêu thích.
        var edit = AuthedRequest(HttpMethod.Patch, $"/api/v1/categories/{createdBody.Id}", accessToken);
        edit.Content = JsonContent.Create(new
        {
            name = "Cà phê", iconName = "coffee", color = "#8FB3E0", parentCategoryId = Guid.Empty, suggestedBudgetCents = 0, isFavorite = false,
        });
        var edited = (await (await _client.SendAsync(edit)).Content.ReadFromJsonAsync<FullCategoryBody>(JsonOptions))!.Data;
        edited.Color.Should().Be("#8FB3E0");
        edited.ParentCategoryId.Should().BeNull();
        edited.SuggestedBudgetCents.Should().BeNull();
        edited.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task CreateWithAParentThatIsAnotherCustomCategory_IsRejected()
    {
        var accessToken = await RegisterAndLoginAsync();
        var first = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        first.Content = JsonContent.Create(new { name = "Cha riêng", iconName = (string?)null });
        var firstBody = (await (await _client.SendAsync(first)).Content.ReadFromJsonAsync<FullCategoryBody>(JsonOptions))!.Data;

        var second = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        second.Content = JsonContent.Create(new { name = "Con", iconName = (string?)null, parentCategoryId = firstBody.Id });
        var response = await _client.SendAsync(second);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("CATEGORY_PARENT_INVALID");
    }

    [Theory]
    [InlineData("color", "red")]
    [InlineData("kind", "transfer")]
    [InlineData("suggestedBudgetCents", -5)]
    public async Task CreateWithInvalidAppearanceValue_IsRejectedAndSavesNothing(string field, object value)
    {
        var accessToken = await RegisterAndLoginAsync();
        var request = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        request.Content = JsonContent.Create(new Dictionary<string, object?> { ["name"] = "Sai giá trị", ["iconName"] = null, [field] = value });

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        var list = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        (await list.Content.ReadFromJsonAsync<FullCategoryListBody>(JsonOptions))!.Data.Should().NotContain(c => c.Name == "Sai giá trị");
    }

    [Fact]
    public async Task CreateUpdateDelete_CustomCategory_FullFlowSucceeds()
    {
        var accessToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        createRequest.Content = JsonContent.Create(new { name = "Cà phê sáng", iconName = "coffee" });
        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResponse.Content.ReadFromJsonAsync<CategoryBody>(JsonOptions);
        createBody!.Data.Slug.Should().Be("ca-phe-sang");
        createBody.Data.IsSystem.Should().BeFalse();
        var categoryId = createBody.Data.Id;

        var updateRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/categories/{categoryId}", accessToken);
        updateRequest.Content = JsonContent.Create(new { name = "Cà phê", iconName = "local_cafe" });
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateBody = await updateResponse.Content.ReadFromJsonAsync<CategoryBody>(JsonOptions);
        updateBody!.Data.Name.Should().Be("Cà phê");
        updateBody.Data.Slug.Should().Be("ca-phe-sang");

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/categories/{categoryId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        var listBody = await listResponse.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);
        listBody!.Data.Should().NotContain(c => c.Id == categoryId);
    }

    [Fact]
    public async Task CreateCategory_DuplicateNameForSameUser_ReturnsConflict()
    {
        var accessToken = await RegisterAndLoginAsync();

        var firstRequest = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        firstRequest.Content = JsonContent.Create(new { name = "Xăng xe", iconName = (string?)null });
        var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondRequest = AuthedRequest(HttpMethod.Post, "/api/v1/categories", accessToken);
        secondRequest.Content = JsonContent.Create(new { name = "Xăng xe", iconName = (string?)null });
        var secondResponse = await _client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateOrDeleteSystemCategory_ReturnsNotFound()
    {
        var accessToken = await RegisterAndLoginAsync();

        var listResponse = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        var listBody = await listResponse.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);
        var systemCategoryId = listBody!.Data.First(c => c.IsSystem).Id;

        var updateRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/categories/{systemCategoryId}", accessToken);
        updateRequest.Content = JsonContent.Create(new { name = "Chiếm đoạt", iconName = (string?)null });
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/categories/{systemCategoryId}", accessToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OtherUsersCustomCategory_UpdateOrDelete_ReturnsNotFound()
    {
        var ownerToken = await RegisterAndLoginAsync();
        var strangerToken = await RegisterAndLoginAsync();

        var createRequest = AuthedRequest(HttpMethod.Post, "/api/v1/categories", ownerToken);
        createRequest.Content = JsonContent.Create(new { name = "Riêng tư", iconName = (string?)null });
        var createResponse = await _client.SendAsync(createRequest);
        var categoryId = (await createResponse.Content.ReadFromJsonAsync<CategoryBody>(JsonOptions))!.Data.Id;

        var updateRequest = AuthedRequest(HttpMethod.Patch, $"/api/v1/categories/{categoryId}", strangerToken);
        updateRequest.Content = JsonContent.Create(new { name = "Chiếm đoạt", iconName = (string?)null });
        var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleteResponse = await _client.SendAsync(
            AuthedRequest(HttpMethod.Delete, $"/api/v1/categories/{categoryId}", strangerToken));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
