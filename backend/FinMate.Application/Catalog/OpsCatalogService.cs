using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Application.Ops;

namespace FinMate.Application.Catalog;

public interface IOpsCatalogService
{
    Task<PagedResult<AdminCategoryDto>> ListCategoriesAsync(Guid actorId, GetSystemCategoryListQuery query, CancellationToken ct = default);
    Task<AdminCategoryDto> CreateCategoryAsync(Guid actorId, string name, string slug, string? iconName, string? ip, CancellationToken ct = default);
    Task<AdminCategoryDto> UpdateCategoryAsync(Guid actorId, Guid categoryId, string? name, string? slug, string? iconName, string? ip, CancellationToken ct = default);
    Task<AdminCategoryDto> SetCategoryActivationAsync(Guid actorId, Guid categoryId, bool isActive, string? ip, CancellationToken ct = default);
    Task<JsonElement?> ReadRulesAsync(Guid actorId, HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default);
    Task<JsonElement?> WriteRuleAsync(Guid actorId, HttpMethod method, string path, string resource, string? key, JsonElement? body, string? ip, CancellationToken ct = default);
}

/// <summary>
/// Phase 95 (B′): Staff duy trì danh mục hệ thống, thương hiệu, từ khoá (đăng ký: "maintain") — sửa trực tiếp, không qua duyệt. Kiểm quyền
/// <see cref="OpsCapability.MaintainCatalog"/> (đọc DB) rồi gọi ĐÚNG handler/service của <c>/admin</c>, nên đồng bộ AI, cache, slug bất biến và lịch sử ở
/// AI Service giữ nguyên; audit mang tiền tố <c>Ops.Catalog.*</c>. Không xoá, không đổi slug, không tắt danh mục được bảo vệ (S4).
/// </summary>
public sealed class OpsCatalogService : IOpsCatalogService
{
    private readonly IOpsAccessService _access;
    private readonly IGetSystemCategoryListQueryHandler _list;
    private readonly ICreateSystemCategoryCommandHandler _create;
    private readonly IUpdateSystemCategoryCommandHandler _update;
    private readonly ISetSystemCategoryActivationCommandHandler _activation;
    private readonly ICategoryRepository _categories;
    private readonly IAdminAiRulesService _rules;

    public OpsCatalogService(
        IOpsAccessService access,
        IGetSystemCategoryListQueryHandler list,
        ICreateSystemCategoryCommandHandler create,
        IUpdateSystemCategoryCommandHandler update,
        ISetSystemCategoryActivationCommandHandler activation,
        ICategoryRepository categories,
        IAdminAiRulesService rules)
    {
        _access = access;
        _list = list;
        _create = create;
        _update = update;
        _activation = activation;
        _categories = categories;
        _rules = rules;
    }

    public async Task<PagedResult<AdminCategoryDto>> ListCategoriesAsync(Guid actorId, GetSystemCategoryListQuery query, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return await _list.HandleAsync(query, ct);
    }

    public async Task<AdminCategoryDto> CreateCategoryAsync(Guid actorId, string name, string slug, string? iconName, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return await _create.HandleAsync(new CreateSystemCategoryCommand(actorId, name, slug, iconName, ip, AuditChannel.Ops), ct);
    }

    public async Task<AdminCategoryDto> UpdateCategoryAsync(Guid actorId, Guid categoryId, string? name, string? slug, string? iconName, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return await _update.HandleAsync(new UpdateSystemCategoryCommand(actorId, categoryId, name, slug, iconName, ip, AuditChannel.Ops), ct);
    }

    public async Task<AdminCategoryDto> SetCategoryActivationAsync(Guid actorId, Guid categoryId, bool isActive, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        if (!isActive && await _categories.GetByIdAsync(categoryId, ct) is { UserId: null } category && ProtectedCategories.IsProtected(category.Slug))
        {
            throw new BusinessRuleException(ProtectedCategories.ErrorCode, $"Danh mục '{category.Slug}' được hệ thống dùng trực tiếp nên không tắt được ở đây.");
        }

        return await _activation.HandleAsync(new SetSystemCategoryActivationCommand(actorId, categoryId, isActive, ip, AuditChannel.Ops), ct);
    }

    public async Task<JsonElement?> ReadRulesAsync(
        Guid actorId, HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, JsonElement? body, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return await _rules.ReadAsync(method, path, query, body, ct);
    }

    public async Task<JsonElement?> WriteRuleAsync(
        Guid actorId, HttpMethod method, string path, string resource, string? key, JsonElement? body, string? ip, CancellationToken ct = default)
    {
        await RequireAsync(actorId, ct);
        return await _rules.WriteAsync(new AdminAiRuleWrite(actorId, method, path, resource, key, null, body, ip, AuditChannel.Ops), ct);
    }

    private Task RequireAsync(Guid actorId, CancellationToken ct) => _access.RequireAsync(actorId, OpsCapability.MaintainCatalog, ct);
}
