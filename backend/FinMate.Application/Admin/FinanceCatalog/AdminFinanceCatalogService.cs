using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;

namespace FinMate.Application.Admin.FinanceCatalog;

public class AdminFinanceCatalogService : IAdminFinanceCatalogService
{
    private const int MaxOptionsPerQuestion = 10;
    private const int MinOptionsPerQuestion = 2;

    private readonly IFinanceMethodRepository _methods;
    private readonly ISurveyRepository _surveys;
    private readonly IAuditLogService _audit;
    private readonly ICategoryRepository _categories;

    public AdminFinanceCatalogService(IFinanceMethodRepository methods, ISurveyRepository surveys, IAuditLogService audit, ICategoryRepository categories)
    {
        _methods = methods;
        _surveys = surveys;
        _audit = audit;
        _categories = categories;
    }

    /// <summary>Mọi danh mục mà các khối "hũ" của bố cục nhắc tới phải là danh mục HỆ THỐNG có thật (danh mục riêng của người dùng không thuộc hũ nào).</summary>
    private async Task ValidateJarCategoriesAsync(FinanceCatalogValidation v, JsonElement layout, CancellationToken ct)
    {
        var slugs = FinanceCatalogValidation.JarCategorySlugs(layout).Distinct().ToList();
        if (slugs.Count == 0)
        {
            return;
        }

        var known = (await _categories.GetSystemListAsync(includeInactive: true, ct)).Select(c => c.Slug).ToHashSet();
        foreach (var slug in slugs.Where(s => !known.Contains(s)))
        {
            v.Add("homeLayout", $"Danh mục '{slug}' không tồn tại (chỉ dùng được mã của danh mục hệ thống).");
        }
    }

    // ================= cách quản lý =================

    public async Task<IReadOnlyList<AdminFinanceMethodDto>> ListMethodsAsync(bool includeInactive, CancellationToken ct = default)
    {
        var activeUsers = await _methods.CountActiveUsersAsync(ct);
        return (await _methods.GetMethodsAsync(includeInactive, ct))
            .OrderBy(m => m.SortOrder)
            .Select(m => ToDto(m, activeUsers.GetValueOrDefault(m.Id)))
            .ToList();
    }

    private async Task<int> ActiveUsersOfAsync(Guid methodId, CancellationToken ct)
        => (await _methods.CountActiveUsersAsync(ct)).GetValueOrDefault(methodId);

    public async Task<AdminFinanceMethodDto> CreateMethodAsync(
        Guid adminId, string? ip, CreateFinanceMethodRequest request, CancellationToken ct = default)
    {
        var v = new FinanceCatalogValidation();
        v.Code("code", request.Code);
        v.Text("name", request.Name, 100);
        v.Text("summary", request.Summary, 400);
        v.Text("whyText", request.WhyText, 1000);
        v.SortOrder("sortOrder", request.SortOrder);
        EnumDefined(v, "role", request.Role);
        EnumDefined(v, "goalMode", request.GoalMode);
        v.ParamDefs("paramDefs", request.ParamDefs);
        v.Eligibility("eligibility", request.Eligibility);
        v.StringArray("starterSteps", request.StarterSteps, 1, 5, 200);
        if (request.HomeLayout is { } homeLayout)
        {
            v.HomeLayout("homeLayout", homeLayout, request.ParamDefs);
            await ValidateJarCategoriesAsync(v, homeLayout, ct);
        }

        v.ThrowIfInvalid();

        if (await _methods.GetMethodByCodeAsync(request.Code, ct) is not null)
        {
            throw new ConflictException(AdminErrorCodes.FinanceMethodCodeDuplicate, $"Đã có cách quản lý với mã '{request.Code}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var method = new FinanceMethod
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name.Trim(),
            Summary = request.Summary.Trim(),
            Role = request.Role,
            GoalMode = request.GoalMode,
            ParamDefs = request.ParamDefs.GetRawText(),
            Eligibility = request.Eligibility.GetRawText(),
            WhyText = request.WhyText.Trim(),
            StarterSteps = request.StarterSteps.GetRawText(),
            HomeLayout = request.HomeLayout?.GetRawText() ?? FinanceHomeBlocks.DefaultLayout,
            SortOrder = request.SortOrder,
            IsActive = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _methods.AddMethodAsync(method, ct);
        await _audit.LogAsync(AuditEvents.AdminFinanceMethodCreated, adminId, ip, new { method.Id, method.Code, method.Role, method.GoalMode }, ct);
        return ToDto(method);
    }

    public async Task<AdminFinanceMethodDto> UpdateMethodAsync(
        Guid adminId, string? ip, Guid id, UpdateFinanceMethodRequest request, CancellationToken ct = default)
    {
        var method = await _methods.GetMethodByIdAsync(id, ct) ?? throw new NotFoundException("FinanceMethod", id);
        RejectCodeChange(request.Code, method.Code, "cách quản lý", "user_methods, prompt AI và liên kết mẫu mục tiêu tra theo nó");

        var v = new FinanceCatalogValidation();
        if (request.Name is not null) { v.Text("name", request.Name, 100); }
        if (request.Summary is not null) { v.Text("summary", request.Summary, 400); }
        if (request.WhyText is not null) { v.Text("whyText", request.WhyText, 1000); }
        if (request.SortOrder is { } order) { v.SortOrder("sortOrder", order); }
        if (request.Role is { } role) { EnumDefined(v, "role", role); }
        if (request.GoalMode is { } goalMode) { EnumDefined(v, "goalMode", goalMode); }
        if (request.ParamDefs is { } paramDefs) { v.ParamDefs("paramDefs", paramDefs); }
        if (request.Eligibility is { } eligibility) { v.Eligibility("eligibility", eligibility); }
        if (request.StarterSteps is { } steps) { v.StringArray("starterSteps", steps, 1, 5, 200); }
        if (request.HomeLayout is not null || request.ParamDefs is not null)
        {
            // Đổi paramDefs có thể làm khối đang lưu trỏ tới khóa không còn nữa: luôn kiểm bố cục HIỆU LỰC với paramDefs HIỆU LỰC.
            var layout = request.HomeLayout ?? FinanceJson.ToElement(method.HomeLayout);
            var defs = request.ParamDefs ?? FinanceJson.ToElement(method.ParamDefs);
            if (layout is { } effectiveLayout && defs is { } effectiveDefs)
            {
                v.HomeLayout("homeLayout", effectiveLayout, effectiveDefs);
                await ValidateJarCategoriesAsync(v, effectiveLayout, ct);
            }
        }

        v.ThrowIfInvalid();

        var changed = new List<string>();
        Apply(changed, "name", request.Name?.Trim(), method.Name, x => method.Name = x);
        Apply(changed, "summary", request.Summary?.Trim(), method.Summary, x => method.Summary = x);
        Apply(changed, "whyText", request.WhyText?.Trim(), method.WhyText, x => method.WhyText = x);
        ApplyValue(changed, "role", request.Role, method.Role, x => method.Role = x);
        ApplyValue(changed, "goalMode", request.GoalMode, method.GoalMode, x => method.GoalMode = x);
        ApplyValue(changed, "sortOrder", request.SortOrder, method.SortOrder, x => method.SortOrder = x);
        ApplyJson(changed, "paramDefs", request.ParamDefs, method.ParamDefs, x => method.ParamDefs = x);
        ApplyJson(changed, "eligibility", request.Eligibility, method.Eligibility, x => method.Eligibility = x);
        ApplyJson(changed, "starterSteps", request.StarterSteps, method.StarterSteps, x => method.StarterSteps = x);
        ApplyJson(changed, "homeLayout", request.HomeLayout, method.HomeLayout, x => method.HomeLayout = x);

        if (changed.Count > 0)
        {
            // Phiên bản tăng mỗi lần nội dung/cơ chế đổi: gợi ý và lựa chọn của người dùng ghi kèm nó để truy ngược.
            method.Version++;
            method.UpdatedAt = DateTimeOffset.UtcNow;
            await _methods.UpdateMethodAsync(method, ct);
            await _audit.LogAsync(AuditEvents.AdminFinanceMethodUpdated, adminId, ip, new { method.Id, method.Code, method.Version, changed }, ct);
        }

        return ToDto(method, await ActiveUsersOfAsync(method.Id, ct));
    }

    public async Task<AdminFinanceMethodDto> SetMethodActiveAsync(
        Guid adminId, string? ip, Guid id, bool isActive, CancellationToken ct = default)
    {
        var method = await _methods.GetMethodByIdAsync(id, ct) ?? throw new NotFoundException("FinanceMethod", id);
        if (method.IsActive != isActive)
        {
            method.IsActive = isActive;
            method.UpdatedAt = DateTimeOffset.UtcNow;
            await _methods.UpdateMethodAsync(method, ct);
            // Tắt một cách đang có người dùng: họ vẫn dùng như cũ, app báo và mời chọn cách khác. Ghi số người bị ảnh hưởng để truy vết.
            var affected = await ActiveUsersOfAsync(method.Id, ct);
            await _audit.LogAsync(AuditEvents.AdminFinanceMethodActivationChanged, adminId, ip, new { method.Id, method.Code, isActive, ActiveUsers = affected }, ct);
        }

        return ToDto(method, await ActiveUsersOfAsync(method.Id, ct));
    }

    public async Task<IReadOnlyList<AdminMethodTemplateLinkDto>> GetMethodTemplatesAsync(Guid methodId, CancellationToken ct = default)
    {
        _ = await _methods.GetMethodByIdAsync(methodId, ct) ?? throw new NotFoundException("FinanceMethod", methodId);
        var templates = (await _methods.GetTemplatesAsync(includeInactive: true, ct)).ToDictionary(t => t.Id);

        return (await _methods.GetLinksForMethodsAsync([methodId], ct))
            .OrderBy(l => l.SortOrder)
            .Where(l => templates.ContainsKey(l.TemplateId))
            .Select(l => new AdminMethodTemplateLinkDto(templates[l.TemplateId].Code, FinanceJson.ToElement(l.Conditions), l.SortOrder))
            .ToList();
    }

    public async Task<IReadOnlyList<AdminMethodTemplateLinkDto>> ReplaceMethodTemplatesAsync(
        Guid adminId, string? ip, Guid methodId, IReadOnlyList<MethodTemplateLinkRequest> links, CancellationToken ct = default)
    {
        var method = await _methods.GetMethodByIdAsync(methodId, ct) ?? throw new NotFoundException("FinanceMethod", methodId);
        var templatesByCode = (await _methods.GetTemplatesAsync(includeInactive: true, ct)).ToDictionary(t => t.Code);

        var v = new FinanceCatalogValidation();
        if (links.GroupBy(l => l.TemplateCode).Any(g => g.Count() > 1))
        {
            v.Add("links", "Mỗi mẫu chỉ được liên kết một lần.");
        }

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (!templatesByCode.ContainsKey(link.TemplateCode))
            {
                v.Add($"links[{i}].templateCode", $"Mẫu '{link.TemplateCode}' không tồn tại.");
            }

            v.SortOrder($"links[{i}].sortOrder", link.SortOrder);
            if (link.Conditions is { ValueKind: not JsonValueKind.Null } conditions)
            {
                ValidateConditions(v, $"links[{i}].conditions", conditions);
            }
        }

        v.ThrowIfInvalid();

        var now = DateTimeOffset.UtcNow;
        var entities = links.Select(l => new MethodGoalTemplate
        {
            MethodId = methodId,
            TemplateId = templatesByCode[l.TemplateCode].Id,
            Conditions = l.Conditions is { ValueKind: not JsonValueKind.Null } c ? c.GetRawText() : null,
            SortOrder = l.SortOrder,
            CreatedAt = now,
        }).ToList();

        await _methods.ReplaceLinksAsync(methodId, entities, ct);
        await _audit.LogAsync(
            AuditEvents.AdminMethodGoalTemplatesReplaced, adminId, ip, new { method.Id, method.Code, templates = links.Select(l => l.TemplateCode) }, ct);

        return await GetMethodTemplatesAsync(methodId, ct);
    }

    // ================= khảo sát =================

    public async Task<IReadOnlyList<AdminSurveyQuestionDto>> ListQuestionsAsync(bool includeInactive, CancellationToken ct = default)
    {
        var questions = (await _surveys.GetQuestionsAsync(includeInactive, ct)).OrderBy(q => q.SortOrder).ToList();
        var options = (await _surveys.GetOptionsAsync(includeInactive: true, ct)).ToLookup(o => o.QuestionId);
        return questions
            .Select(q => ToDto(q, options[q.Id].Where(o => includeInactive || o.IsActive)))
            .ToList();
    }

    public async Task<AdminSurveyQuestionDto> CreateQuestionAsync(
        Guid adminId, string? ip, CreateSurveyQuestionRequest request, CancellationToken ct = default)
    {
        var existing = await _surveys.GetQuestionsAsync(includeInactive: true, ct);

        var v = new FinanceCatalogValidation();
        v.Code("code", request.Code);
        v.Code("groupCode", request.GroupCode);
        v.Text("text", request.Text, 500);
        v.SortOrder("sortOrder", request.SortOrder);
        if (request.ShowIf is { ValueKind: not JsonValueKind.Null } showIf)
        {
            v.ShowIf("showIf", showIf, existing, request.SortOrder, request.Code);
        }

        ValidateNewOptions(v, request.Options);
        v.ThrowIfInvalid();

        if (existing.Any(q => q.Code == request.Code))
        {
            throw new ConflictException(AdminErrorCodes.SurveyQuestionCodeDuplicate, $"Đã có câu hỏi với mã '{request.Code}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var question = new SurveyQuestion
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            GroupCode = request.GroupCode,
            Text = request.Text.Trim(),
            ShowIf = request.ShowIf is { ValueKind: not JsonValueKind.Null } s ? s.GetRawText() : null,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var options = request.Options.Select(o => NewOption(question.Id, o, now)).ToList();

        await _surveys.AddQuestionAsync(question, options, ct);
        await _audit.LogAsync(
            AuditEvents.AdminSurveyQuestionCreated, adminId, ip, new { question.Id, question.Code, options = options.Count }, ct);
        return ToDto(question, options);
    }

    public async Task<AdminSurveyQuestionDto> UpdateQuestionAsync(
        Guid adminId, string? ip, Guid id, UpdateSurveyQuestionRequest request, CancellationToken ct = default)
    {
        var question = await _surveys.GetQuestionByIdAsync(id, ct) ?? throw new NotFoundException("SurveyQuestion", id);
        RejectCodeChange(request.Code, question.Code, "câu hỏi", "câu trả lời của người dùng lưu theo nó");

        var sortOrder = request.SortOrder ?? question.SortOrder;
        var v = new FinanceCatalogValidation();
        if (request.GroupCode is not null) { v.Code("groupCode", request.GroupCode); }
        if (request.Text is not null) { v.Text("text", request.Text, 500); }
        if (request.SortOrder is { } order) { v.SortOrder("sortOrder", order); }
        if (request.ShowIf is { ValueKind: not JsonValueKind.Null } showIf)
        {
            v.ShowIf("showIf", showIf, await _surveys.GetQuestionsAsync(includeInactive: true, ct), sortOrder, question.Code);
        }

        v.ThrowIfInvalid();

        var changed = new List<string>();
        Apply(changed, "groupCode", request.GroupCode, question.GroupCode, x => question.GroupCode = x);
        Apply(changed, "text", request.Text?.Trim(), question.Text, x => question.Text = x);
        ApplyValue(changed, "sortOrder", request.SortOrder, question.SortOrder, x => question.SortOrder = x);
        if (request.ClearShowIf == true && question.ShowIf is not null)
        {
            question.ShowIf = null;
            changed.Add("showIf");
        }
        else if (request.ShowIf is { ValueKind: not JsonValueKind.Null } newShowIf && !JsonEquality.AreEqual(question.ShowIf, newShowIf))
        {
            question.ShowIf = newShowIf.GetRawText();
            changed.Add("showIf");
        }

        if (changed.Count > 0)
        {
            question.UpdatedAt = DateTimeOffset.UtcNow;
            await _surveys.UpdateQuestionAsync(question, ct);
            await _audit.LogAsync(AuditEvents.AdminSurveyQuestionUpdated, adminId, ip, new { question.Id, question.Code, changed }, ct);
        }

        return await LoadQuestionAsync(question, ct);
    }

    public async Task<AdminSurveyQuestionDto> SetQuestionActiveAsync(
        Guid adminId, string? ip, Guid id, bool isActive, CancellationToken ct = default)
    {
        var question = await _surveys.GetQuestionByIdAsync(id, ct) ?? throw new NotFoundException("SurveyQuestion", id);
        if (question.IsActive != isActive)
        {
            question.IsActive = isActive;
            question.UpdatedAt = DateTimeOffset.UtcNow;
            await _surveys.UpdateQuestionAsync(question, ct);
            await _audit.LogAsync(AuditEvents.AdminSurveyQuestionActivationChanged, adminId, ip, new { question.Id, question.Code, isActive }, ct);
        }

        return await LoadQuestionAsync(question, ct);
    }

    public async Task<AdminSurveyQuestionDto> AddOptionAsync(
        Guid adminId, string? ip, Guid questionId, CreateSurveyOptionRequest request, CancellationToken ct = default)
    {
        var question = await _surveys.GetQuestionByIdAsync(questionId, ct) ?? throw new NotFoundException("SurveyQuestion", questionId);

        var v = new FinanceCatalogValidation();
        ValidateOption(v, "option", request);
        v.ThrowIfInvalid();

        var siblings = await _surveys.GetOptionsAsync(includeInactive: true, ct);
        if (siblings.Any(o => o.QuestionId == questionId && o.Code == request.Code))
        {
            throw new ConflictException(AdminErrorCodes.SurveyOptionCodeDuplicate, $"Câu hỏi đã có đáp án '{request.Code}'.");
        }

        var option = NewOption(questionId, request, DateTimeOffset.UtcNow);
        await _surveys.AddOptionAsync(option, ct);
        await _audit.LogAsync(AuditEvents.AdminSurveyOptionCreated, adminId, ip, new { QuestionCode = question.Code, option.Id, option.Code }, ct);
        return await LoadQuestionAsync(question, ct);
    }

    public async Task<AdminSurveyQuestionDto> UpdateOptionAsync(
        Guid adminId, string? ip, Guid questionId, Guid optionId, UpdateSurveyOptionRequest request, CancellationToken ct = default)
    {
        var (question, option) = await LoadOptionAsync(questionId, optionId, ct);
        RejectCodeChange(request.Code, option.Code, "đáp án", "câu trả lời của người dùng lưu theo nó");

        var v = new FinanceCatalogValidation();
        if (request.Text is not null) { v.Text("text", request.Text, 500); }
        if (request.Signals is { } signals) { v.SignalMap("signals", signals); }
        if (request.SortOrder is { } order) { v.SortOrder("sortOrder", order); }
        v.ThrowIfInvalid();

        var changed = new List<string>();
        Apply(changed, "text", request.Text?.Trim(), option.Text, x => option.Text = x);
        ApplyValue(changed, "sortOrder", request.SortOrder, option.SortOrder, x => option.SortOrder = x);
        ApplyJson(changed, "signals", request.Signals, option.Signals, x => option.Signals = x);

        if (changed.Count > 0)
        {
            option.UpdatedAt = DateTimeOffset.UtcNow;
            await _surveys.UpdateOptionAsync(option, ct);
            await _audit.LogAsync(AuditEvents.AdminSurveyOptionUpdated, adminId, ip, new { QuestionCode = question.Code, option.Id, option.Code, changed }, ct);
        }

        return await LoadQuestionAsync(question, ct);
    }

    public async Task<AdminSurveyQuestionDto> SetOptionActiveAsync(
        Guid adminId, string? ip, Guid questionId, Guid optionId, bool isActive, CancellationToken ct = default)
    {
        var (question, option) = await LoadOptionAsync(questionId, optionId, ct);
        if (option.IsActive != isActive)
        {
            option.IsActive = isActive;
            option.UpdatedAt = DateTimeOffset.UtcNow;
            await _surveys.UpdateOptionAsync(option, ct);
            await _audit.LogAsync(AuditEvents.AdminSurveyOptionActivationChanged, adminId, ip, new { QuestionCode = question.Code, option.Id, option.Code, isActive }, ct);
        }

        return await LoadQuestionAsync(question, ct);
    }

    // ================= mẫu mục tiêu =================

    public async Task<IReadOnlyList<AdminGoalTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct = default)
        => (await _methods.GetTemplatesAsync(includeInactive, ct)).OrderBy(t => t.Code).Select(ToDto).ToList();

    public async Task<AdminGoalTemplateDto> CreateTemplateAsync(
        Guid adminId, string? ip, CreateGoalTemplateRequest request, CancellationToken ct = default)
    {
        var v = new FinanceCatalogValidation();
        v.Code("code", request.Code);
        v.Text("name", request.Name, 200);
        v.Text("description", request.Description, 500);
        v.IntegerObject("defaultParams", request.DefaultParams);
        v.ThrowIfInvalid();

        if (await _methods.GetTemplateByCodeAsync(request.Code, ct) is not null)
        {
            throw new ConflictException(AdminErrorCodes.GoalTemplateCodeDuplicate, $"Đã có mẫu mục tiêu với mã '{request.Code}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var template = new GoalTemplate
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            DefaultParams = request.DefaultParams.GetRawText(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _methods.AddTemplateAsync(template, ct);
        await _audit.LogAsync(AuditEvents.AdminGoalTemplateCreated, adminId, ip, new { template.Id, template.Code }, ct);
        return ToDto(template);
    }

    public async Task<AdminGoalTemplateDto> UpdateTemplateAsync(
        Guid adminId, string? ip, Guid id, UpdateGoalTemplateRequest request, CancellationToken ct = default)
    {
        var template = await _methods.GetTemplateByIdAsync(id, ct) ?? throw new NotFoundException("GoalTemplate", id);
        RejectCodeChange(request.Code, template.Code, "mẫu mục tiêu", "liên kết với phương pháp tra theo nó");

        var v = new FinanceCatalogValidation();
        if (request.Name is not null) { v.Text("name", request.Name, 200); }
        if (request.Description is not null) { v.Text("description", request.Description, 500); }
        if (request.DefaultParams is { } defaults) { v.IntegerObject("defaultParams", defaults); }
        v.ThrowIfInvalid();

        var changed = new List<string>();
        Apply(changed, "name", request.Name?.Trim(), template.Name, x => template.Name = x);
        Apply(changed, "description", request.Description?.Trim(), template.Description, x => template.Description = x);
        ApplyJson(changed, "defaultParams", request.DefaultParams, template.DefaultParams, x => template.DefaultParams = x);

        if (changed.Count > 0)
        {
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _methods.UpdateTemplateAsync(template, ct);
            await _audit.LogAsync(AuditEvents.AdminGoalTemplateUpdated, adminId, ip, new { template.Id, template.Code, changed }, ct);
        }

        return ToDto(template);
    }

    public async Task<AdminGoalTemplateDto> SetTemplateActiveAsync(
        Guid adminId, string? ip, Guid id, bool isActive, CancellationToken ct = default)
    {
        var template = await _methods.GetTemplateByIdAsync(id, ct) ?? throw new NotFoundException("GoalTemplate", id);
        if (template.IsActive != isActive)
        {
            template.IsActive = isActive;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _methods.UpdateTemplateAsync(template, ct);
            await _audit.LogAsync(AuditEvents.AdminGoalTemplateActivationChanged, adminId, ip, new { template.Id, template.Code, isActive }, ct);
        }

        return ToDto(template);
    }

    // ================= phụ trợ =================

    private async Task<(SurveyQuestion Question, SurveyOption Option)> LoadOptionAsync(Guid questionId, Guid optionId, CancellationToken ct)
    {
        var question = await _surveys.GetQuestionByIdAsync(questionId, ct) ?? throw new NotFoundException("SurveyQuestion", questionId);
        var option = await _surveys.GetOptionByIdAsync(optionId, ct);
        if (option is null || option.QuestionId != questionId)
        {
            throw new NotFoundException("SurveyOption", optionId);
        }

        return (question, option);
    }

    private async Task<AdminSurveyQuestionDto> LoadQuestionAsync(SurveyQuestion question, CancellationToken ct)
        => ToDto(question, (await _surveys.GetOptionsAsync(includeInactive: true, ct)).Where(o => o.QuestionId == question.Id));

    private static SurveyOption NewOption(Guid questionId, CreateSurveyOptionRequest request, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        QuestionId = questionId,
        Code = request.Code,
        Text = request.Text.Trim(),
        Signals = request.Signals.GetRawText(),
        SortOrder = request.SortOrder,
        IsActive = true,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static void ValidateOption(FinanceCatalogValidation v, string field, CreateSurveyOptionRequest option)
    {
        v.OptionCode($"{field}.code", option.Code);
        v.Text($"{field}.text", option.Text, 500);
        v.SignalMap($"{field}.signals", option.Signals);
        v.SortOrder($"{field}.sortOrder", option.SortOrder);
    }

    private static void ValidateNewOptions(FinanceCatalogValidation v, IReadOnlyList<CreateSurveyOptionRequest> options)
    {
        if (options.Count < MinOptionsPerQuestion || options.Count > MaxOptionsPerQuestion)
        {
            v.Add("options", $"Một câu hỏi phải có từ {MinOptionsPerQuestion} đến {MaxOptionsPerQuestion} đáp án.");
        }

        if (options.GroupBy(o => o.Code).Any(g => g.Count() > 1))
        {
            v.Add("options", "Mã đáp án không được trùng nhau trong cùng một câu hỏi.");
        }

        for (var i = 0; i < options.Count; i++)
        {
            ValidateOption(v, $"options[{i}]", options[i]);
        }
    }

    private static void ValidateConditions(FinanceCatalogValidation v, string field, JsonElement conditions)
    {
        if (conditions.ValueKind != JsonValueKind.Object)
        {
            v.Add(field, "Phải là một object.");
            return;
        }

        foreach (var property in conditions.EnumerateObject())
        {
            if (property.Name == "excludes")
            {
                v.SignalList($"{field}.excludes", property.Value);
            }
            else
            {
                v.Add(field, $"Trường '{property.Name}' không được hỗ trợ (chỉ có excludes).");
            }
        }
    }

    private static void EnumDefined<T>(FinanceCatalogValidation v, string field, T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            v.Add(field, "Giá trị không hợp lệ.");
        }
    }

    /// <summary>Khóa định danh bất biến: các bảng khác join vào, đổi là làm hỏng liên kết một cách im lặng thay vì ồn ào.</summary>
    private static void RejectCodeChange(string? requested, string current, string what, string reason)
    {
        if (requested is not null && !string.Equals(requested.Trim(), current, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(
                AdminErrorCodes.ImmutableField, $"Không đổi được mã {what}: {reason}. Tạo mới và tắt cái cũ.");
        }
    }

    private static void Apply<T>(List<string> changed, string field, T? requested, T current, Action<T> set) where T : notnull
    {
        if (requested is { } value && !EqualityComparer<T>.Default.Equals(value, current))
        {
            set(value);
            changed.Add(field);
        }
    }

    private static void ApplyValue<T>(List<string> changed, string field, T? requested, T current, Action<T> set) where T : struct
    {
        if (requested is { } value && !EqualityComparer<T>.Default.Equals(value, current))
        {
            set(value);
            changed.Add(field);
        }
    }

    private static void ApplyJson(List<string> changed, string field, JsonElement? requested, string current, Action<string> set)
    {
        if (requested is { } value && !JsonEquality.AreEqual(current, value))
        {
            set(value.GetRawText());
            changed.Add(field);
        }
    }

    private static AdminFinanceMethodDto ToDto(FinanceMethod m, int activeUsers = 0) => new(
        m.Id,
        m.Code,
        m.Name,
        m.Summary,
        FinanceMapper.RoleText(m.Role),
        FinanceMapper.GoalModeText(m.GoalMode),
        FinanceJson.ToElement(m.ParamDefs),
        FinanceJson.ToElement(m.Eligibility),
        m.WhyText,
        FinanceJson.ToElement(m.StarterSteps),
        FinanceJson.ToElement(m.HomeLayout),
        m.SortOrder,
        m.IsActive,
        m.Version,
        m.CreatedAt,
        m.UpdatedAt,
        activeUsers);

    private static AdminSurveyQuestionDto ToDto(SurveyQuestion q, IEnumerable<SurveyOption> options) => new(
        q.Id,
        q.Code,
        q.GroupCode,
        q.Text,
        FinanceJson.ToElement(q.ShowIf),
        q.SortOrder,
        q.IsActive,
        options.OrderBy(o => o.SortOrder).Select(o => new AdminSurveyOptionDto(
            o.Id, o.Code, o.Text, FinanceJson.ToElement(o.Signals), o.SortOrder, o.IsActive)).ToList());

    private static AdminGoalTemplateDto ToDto(GoalTemplate t) => new(
        t.Id, t.Code, t.Name, t.Description, FinanceJson.ToElement(t.DefaultParams), t.IsActive, t.CreatedAt, t.UpdatedAt);
}

/// <summary>So sánh hai JSON theo CẤU TRÚC (không theo chuỗi): Postgres <c>jsonb</c> chuẩn hóa khoảng trắng và thứ tự khóa nên chuỗi lưu khác chuỗi gửi lên dù cùng nội dung.</summary>
internal static class JsonEquality
{
    public static bool AreEqual(string? stored, JsonElement requested)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(stored);
            return AreEqual(doc.RootElement, requested);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool AreEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                return left.Count == right.Count && left.All(kv => right.TryGetValue(kv.Key, out var other) && AreEqual(kv.Value, other));
            case JsonValueKind.Array:
                var xs = a.EnumerateArray().ToList();
                var ys = b.EnumerateArray().ToList();
                return xs.Count == ys.Count && xs.Zip(ys).All(p => AreEqual(p.First, p.Second));
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();
            default:
                return true;   // true / false / null: cùng ValueKind là bằng nhau
        }
    }
}
