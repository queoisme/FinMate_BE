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
    public async Task GetList_IncludesAllElevenSystemCategories()
    {
        var accessToken = await RegisterAndLoginAsync();

        var response = await _client.SendAsync(AuthedRequest(HttpMethod.Get, "/api/v1/categories", accessToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CategoryListBody>(JsonOptions);

        body!.Data.Count(c => c.IsSystem).Should().Be(11);
        body.Data.Select(c => c.Slug).Should().Contain(new[] { "food", "income", "other" });
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
