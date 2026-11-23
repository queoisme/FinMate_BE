using System.Text.Json;
using System.Text.RegularExpressions;
using FinMate.Application.Budgets;
using FinMate.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.Admin.FinanceCatalog;

/// <summary>
/// Kiểm tra dữ liệu admin nhập cho danh mục cách quản lý tài chính. Cần kiểm kỹ hơn CHECK của DB (chỉ biết "object" hay "array"): một
/// <c>param_defs</c> hay <c>eligibility</c> sai hình dạng sẽ lưu được rồi làm phương pháp hỏng LẶNG LẼ ở mọi người dùng chọn nó.
/// Gom MỌI lỗi vào một <see cref="ValidationException"/> để admin sửa một lượt, không phải vá từng lỗi một.
/// </summary>
internal sealed class FinanceCatalogValidation
{
    private static readonly Regex CodeFormat = new("^[a-z][a-z0-9_]{0,47}$", RegexOptions.Compiled);
    private static readonly Regex OptionCodeFormat = new("^[A-Za-z0-9_]{1,10}$", RegexOptions.Compiled);

    public const int MaxSignalWeight = 10;

    private readonly List<ValidationFailure> _failures = [];

    public void ThrowIfInvalid()
    {
        if (_failures.Count > 0)
        {
            throw new ValidationException(_failures);
        }
    }

    public void Code(string field, string? value)
    {
        if (value is null || !CodeFormat.IsMatch(value))
        {
            Add(field, "Mã chỉ gồm chữ thường, số và dấu gạch dưới, bắt đầu bằng chữ, tối đa 48 ký tự.");
        }
    }

    public void OptionCode(string field, string? value)
    {
        if (value is null || !OptionCodeFormat.IsMatch(value))
        {
            Add(field, "Mã đáp án chỉ gồm chữ, số và dấu gạch dưới, tối đa 10 ký tự.");
        }
    }

    public void Text(string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, "Không được để trống.");
        }
        else if (value.Trim().Length > max)
        {
            Add(field, $"Tối đa {max} ký tự.");
        }
    }

    public void SortOrder(string field, int value)
    {
        if (value < 0)
        {
            Add(field, "Thứ tự không được âm.");
        }
    }

    /// <summary>Mảng [{key, label, type, default, min, max, userEditable}] với khóa duy nhất, min ≤ default ≤ max và T1/T2 đi cặp hợp lệ.</summary>
    public void ParamDefs(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(field, "Phải là một mảng.");
            return;
        }

        var keys = new HashSet<string>();
        var byKey = new Dictionary<string, (long Default, long Min, long Max)>();
        var index = 0;
        foreach (var def in value.EnumerateArray())
        {
            var where = $"{field}[{index++}]";
            if (def.ValueKind != JsonValueKind.Object)
            {
                Add(where, "Mỗi tham số phải là một object.");
                continue;
            }

            var key = StringProp(def, "key");
            if (key is null || !CodeFormat.IsMatch(key))
            {
                Add($"{where}.key", "Khóa tham số chỉ gồm chữ thường, số và dấu gạch dưới.");
            }
            else if (!keys.Add(key))
            {
                Add($"{where}.key", $"Khóa '{key}' bị trùng.");
            }

            if (string.IsNullOrWhiteSpace(StringProp(def, "label")) || StringProp(def, "label")!.Length > 100)
            {
                Add($"{where}.label", "Nhãn không được trống và tối đa 100 ký tự.");
            }

            var type = StringProp(def, "type");
            if (type is not (FinanceParamTypes.Percent or FinanceParamTypes.Int or FinanceParamTypes.Cents or FinanceParamTypes.Minutes))
            {
                Add($"{where}.type", "Kiểu phải là percent, int, cents hoặc minutes.");
            }

            var hasNumbers = IntProp(def, "default", out var value0) & IntProp(def, "min", out var min) & IntProp(def, "max", out var max);
            if (!hasNumbers)
            {
                Add(where, "default, min, max phải là số nguyên (không dùng số thập phân).");
            }
            else if (min > max || value0 < min || value0 > max)
            {
                Add(where, "Phải có min ≤ default ≤ max.");
            }
            else if (key is not null)
            {
                byKey[key] = (value0, min, max);
            }

            if (!def.TryGetProperty("userEditable", out var editable) || editable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Add($"{where}.userEditable", "Phải là true hoặc false.");
            }
        }

        // T1/T2 đi cặp: thiếu một cái thì cảnh báo ngân sách không biết dùng ngưỡng nào; và mặc định + khoảng cho phép phải giữ được 1 ≤ T1 < T2 ≤ 99.
        var hasT1 = byKey.TryGetValue(FinanceParamKeys.T1Pct, out var t1);
        var hasT2 = byKey.TryGetValue(FinanceParamKeys.T2Pct, out var t2);
        if (hasT1 != hasT2)
        {
            Add(field, "t1_pct và t2_pct phải đi cùng nhau.");
        }
        else if (hasT1)
        {
            if (!AlertThresholds.IsValid(new AlertThresholds((int)t1.Default, (int)t2.Default)))
            {
                Add(field, "Mặc định của t1_pct và t2_pct phải thỏa 1 ≤ T1 < T2 ≤ 99.");
            }

            if (t1.Min < AlertThresholds.MinT1Pct || t2.Max > 99)
            {
                Add(field, "t1_pct tối thiểu 1 và t2_pct tối đa 99 (100% là mức 'vượt hạn mức' cố định).");
            }
        }
    }

    /// <summary>
    /// Mảng 1–8 khối [{type, title?, keys?, display?}] (loại khối: <see cref="FinanceHomeBlocks"/>). Khối sai hình dạng sẽ làm trang chủ của MỌI người đang dùng cách này vẽ hỏng,
    /// nên <c>keys</c> phải là khóa có trong <paramref name="paramDefs"/> và <c>allocation</c> chỉ cho tham số kiểu percent.
    /// </summary>
    public void HomeLayout(string field, JsonElement value, JsonElement paramDefs)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(field, "Phải là một mảng khối.");
            return;
        }

        var paramTypes = ParamTypeByKey(paramDefs);
        var blocks = value.EnumerateArray().ToList();
        if (blocks.Count < 1 || blocks.Count > FinanceHomeBlocks.MaxBlocks)
        {
            Add(field, $"Phải có từ 1 đến {FinanceHomeBlocks.MaxBlocks} khối.");
        }

        var seenTypes = new HashSet<string>();
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var where = $"{field}[{index}]";
            if (block.ValueKind != JsonValueKind.Object)
            {
                Add(where, "Mỗi khối phải là một object.");
                continue;
            }

            foreach (var property in block.EnumerateObject().Where(p => p.Name is not ("type" or "title" or "keys" or "display" or "jars")))
            {
                Add(where, $"Trường '{property.Name}' không được hỗ trợ (chỉ có type, title, keys, display, jars).");
            }

            var type = StringProp(block, "type");
            if (type is null || !FinanceHomeBlocks.Types.Contains(type))
            {
                Add($"{where}.type", $"Loại khối phải là một trong: {string.Join(", ", FinanceHomeBlocks.Types)}.");
                continue;
            }

            if (type != FinanceHomeBlocks.Params && !seenTypes.Add(type))
            {
                Add($"{where}.type", $"Khối '{type}' chỉ được xuất hiện một lần.");
            }

            if (block.TryGetProperty("title", out var title)
                && (title.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title.GetString()) || title.GetString()!.Length > FinanceHomeBlocks.MaxTitleLength))
            {
                Add($"{where}.title", $"Tiêu đề là chuỗi không trống, tối đa {FinanceHomeBlocks.MaxTitleLength} ký tự.");
            }

            var hasKeys = block.TryGetProperty("keys", out var keys);
            var hasDisplay = block.TryGetProperty("display", out var display);
            if (type == FinanceHomeBlocks.Jars)
            {
                if (hasKeys || hasDisplay)
                {
                    Add(where, "keys và display không dùng cho khối 'jars'.");
                }

                JarsBlock(where, block, paramDefs, paramTypes);
                continue;
            }

            if (block.TryGetProperty("jars", out _))
            {
                Add(where, $"jars chỉ dùng cho khối '{FinanceHomeBlocks.Jars}'.");
            }

            if (type != FinanceHomeBlocks.Params)
            {
                if (hasKeys || hasDisplay)
                {
                    Add(where, $"keys và display chỉ dùng cho khối '{FinanceHomeBlocks.Params}'.");
                }

                continue;
            }

            var chosen = new List<string>();
            if (hasKeys)
            {
                if (keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() == 0 || keys.GetArrayLength() > 12
                    || keys.EnumerateArray().Any(k => k.ValueKind != JsonValueKind.String))
                {
                    Add($"{where}.keys", "Phải là mảng 1–12 khóa tham số.");
                }
                else
                {
                    chosen = keys.EnumerateArray().Select(k => k.GetString()!).ToList();
                    if (chosen.Distinct().Count() != chosen.Count)
                    {
                        Add($"{where}.keys", "Khóa tham số bị trùng.");
                    }

                    foreach (var key in chosen.Where(k => !paramTypes.ContainsKey(k)))
                    {
                        Add($"{where}.keys", $"Tham số '{key}' không có trong paramDefs của cách quản lý này.");
                    }
                }
            }

            if (!hasDisplay)
            {
                continue;
            }

            var mode = display.ValueKind == JsonValueKind.String ? display.GetString() : null;
            if (mode is not (FinanceHomeBlocks.DisplayList or FinanceHomeBlocks.DisplayAllocation))
            {
                Add($"{where}.display", $"Phải là '{FinanceHomeBlocks.DisplayList}' hoặc '{FinanceHomeBlocks.DisplayAllocation}'.");
            }
            else if (mode == FinanceHomeBlocks.DisplayAllocation)
            {
                var targets = hasKeys ? chosen : paramTypes.Keys.ToList();
                var percentTargets = targets.Where(k => paramTypes.GetValueOrDefault(k) == FinanceParamTypes.Percent).ToList();
                if (percentTargets.Count == 0 || (hasKeys && percentTargets.Count != targets.Count))
                {
                    Add($"{where}.display", "Dạng 'allocation' chỉ dùng cho tham số kiểu percent và phải có ít nhất một tham số như vậy.");
                }
            }
        }
    }

    private static readonly Regex CategorySlugFormat = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// Khối "hũ": mỗi hũ gắn với MỘT tham số kiểu % của cách quản lý (phần trăm thu nhập chia cho hũ) và một danh sách danh mục. Một tham số chỉ thuộc một hũ, một danh mục chỉ thuộc
    /// một hũ (nếu không chi tiêu sẽ bị tính hai lần), và tổng tỷ lệ MẶC ĐỊNH của các hũ không quá 100%. Danh mục có thật hay không do service kiểm (cần đọc DB).
    /// </summary>
    private void JarsBlock(string where, JsonElement block, JsonElement paramDefs, Dictionary<string, string> paramTypes)
    {
        if (!block.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array
            || jars.GetArrayLength() < 1 || jars.GetArrayLength() > FinanceHomeBlocks.MaxJars)
        {
            Add($"{where}.jars", $"Phải là mảng từ 1 đến {FinanceHomeBlocks.MaxJars} hũ.");
            return;
        }

        var defaults = ParamDefaultByKey(paramDefs);
        var usedKeys = new HashSet<string>();
        var usedSlugs = new HashSet<string>();
        long defaultSum = 0;
        var index = 0;
        foreach (var jar in jars.EnumerateArray())
        {
            var path = $"{where}.jars[{index++}]";
            if (jar.ValueKind != JsonValueKind.Object)
            {
                Add(path, "Mỗi hũ phải là một object.");
                continue;
            }

            foreach (var property in jar.EnumerateObject().Where(p => p.Name is not ("key" or "label" or "categories")))
            {
                Add(path, $"Trường '{property.Name}' không được hỗ trợ (chỉ có key, label, categories).");
            }

            var key = StringProp(jar, "key");
            if (key is null || paramTypes.GetValueOrDefault(key) != FinanceParamTypes.Percent)
            {
                Add($"{path}.key", "Hũ phải gắn với một tham số kiểu % có trong paramDefs của cách quản lý này.");
            }
            else if (key is FinanceParamKeys.T1Pct or FinanceParamKeys.T2Pct)
            {
                // T1/T2 cũng là tham số kiểu %, nhưng là ngưỡng cảnh báo chứ không phải phần chia thu nhập.
                Add($"{path}.key", "Ngưỡng cảnh báo (T1/T2) không dùng làm phần chia của hũ.");
            }
            else if (!usedKeys.Add(key))
            {
                Add($"{path}.key", $"Tham số '{key}' đã dùng cho hũ khác.");
            }
            else
            {
                defaultSum += defaults.GetValueOrDefault(key);
            }

            if (jar.TryGetProperty("label", out var label)
                && (label.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(label.GetString()) || label.GetString()!.Length > FinanceHomeBlocks.MaxJarLabelLength))
            {
                Add($"{path}.label", $"Tên hũ là chuỗi không trống, tối đa {FinanceHomeBlocks.MaxJarLabelLength} ký tự.");
            }

            if (!jar.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array
                || categories.GetArrayLength() > FinanceHomeBlocks.MaxCategoriesPerJar
                || categories.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.String || !CategorySlugFormat.IsMatch(c.GetString()!)))
            {
                Add($"{path}.categories", $"Phải là mảng tối đa {FinanceHomeBlocks.MaxCategoriesPerJar} mã danh mục (chữ thường, số, dấu gạch nối).");
                continue;
            }

            foreach (var slug in categories.EnumerateArray().Select(c => c.GetString()!))
            {
                if (!usedSlugs.Add(slug))
                {
                    Add($"{path}.categories", $"Danh mục '{slug}' đã thuộc một hũ (hoặc bị lặp); mỗi danh mục chỉ thuộc một hũ.");
                }
            }
        }

        if (defaultSum > 100)
        {
            Add($"{where}.jars", $"Tổng tỷ lệ mặc định của các hũ là {defaultSum}%, vượt 100%.");
        }
    }

    /// <summary>Mọi mã danh mục được các khối "hũ" của một bố cục nhắc tới (để service kiểm chúng có thật).</summary>
    public static IReadOnlyList<string> JarCategorySlugs(JsonElement layout)
    {
        var slugs = new List<string>();
        if (layout.ValueKind != JsonValueKind.Array)
        {
            return slugs;
        }

        foreach (var block in layout.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.Object && StringProp(b, "type") == FinanceHomeBlocks.Jars))
        {
            if (!block.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var jar in jars.EnumerateArray().Where(j => j.ValueKind == JsonValueKind.Object))
            {
                if (jar.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
                {
                    slugs.AddRange(categories.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!));
                }
            }
        }

        return slugs;
    }

    private static Dictionary<string, long> ParamDefaultByKey(JsonElement paramDefs)
    {
        var defaults = new Dictionary<string, long>();
        if (paramDefs.ValueKind != JsonValueKind.Array)
        {
            return defaults;
        }

        foreach (var def in paramDefs.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Object))
        {
            if (StringProp(def, "key") is { } key && IntProp(def, "default", out var value))
            {
                defaults[key] = value;
            }
        }

        return defaults;
    }

    private static Dictionary<string, string> ParamTypeByKey(JsonElement paramDefs)
    {
        var types = new Dictionary<string, string>();
        if (paramDefs.ValueKind != JsonValueKind.Array)
        {
            return types;
        }

        foreach (var def in paramDefs.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Object))
        {
            if (StringProp(def, "key") is { } key)
            {
                types[key] = StringProp(def, "type") ?? string.Empty;
            }
        }

        return types;
    }

    /// <summary>{"weights":{signal:trọng số nguyên 1–10},"excludes":[signal]} — cả hai trường đều tùy chọn nhưng đúng kiểu khi có.</summary>
    public void Eligibility(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            Add(field, "Phải là một object.");
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            switch (property.Name)
            {
                case "weights":
                    SignalMap($"{field}.weights", property.Value);
                    break;
                case "excludes":
                    SignalList($"{field}.excludes", property.Value);
                    break;
                default:
                    Add(field, $"Trường '{property.Name}' không được hỗ trợ (chỉ có weights và excludes).");
                    break;
            }
        }
    }

    /// <summary>Object {signal: trọng số nguyên 1–10}; signal đặt tên như mã.</summary>
    public void SignalMap(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            Add(field, "Phải là một object {signal: trọng số}.");
            return;
        }

        foreach (var item in value.EnumerateObject())
        {
            if (!CodeFormat.IsMatch(item.Name))
            {
                Add(field, $"Tên signal '{item.Name}' không hợp lệ.");
            }

            if (item.Value.ValueKind != JsonValueKind.Number || !item.Value.TryGetInt32(out var weight) || weight < 1 || weight > MaxSignalWeight)
            {
                Add($"{field}.{item.Name}", $"Trọng số phải là số nguyên từ 1 đến {MaxSignalWeight}.");
            }
        }
    }

    public void SignalList(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(field, "Phải là một mảng tên signal.");
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !CodeFormat.IsMatch(item.GetString()!))
            {
                Add(field, "Mỗi phần tử phải là một tên signal hợp lệ.");
            }
        }
    }

    public void StringArray(string field, JsonElement value, int minItems, int maxItems, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(field, "Phải là một mảng chuỗi.");
            return;
        }

        var items = value.EnumerateArray().ToList();
        if (items.Count < minItems || items.Count > maxItems)
        {
            Add(field, $"Phải có từ {minItems} đến {maxItems} phần tử.");
        }

        if (items.Any(i => i.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(i.GetString()) || i.GetString()!.Length > maxLength))
        {
            Add(field, $"Mỗi phần tử là chuỗi không trống, tối đa {maxLength} ký tự.");
        }
    }

    /// <summary>Object bất kỳ (vd <c>default_params</c> của mẫu mục tiêu); mọi giá trị số phải là số nguyên — số tiền/phần trăm không dùng float.</summary>
    public void IntegerObject(string field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            Add(field, "Phải là một object.");
            return;
        }

        foreach (var item in value.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.Number && !item.Value.TryGetInt64(out _))
            {
                Add($"{field}.{item.Name}", "Số phải là số nguyên.");
            }
        }
    }

    /// <summary>{"all":[{"question":mã,"in"|"not_in":[mã đáp án]}]}. Câu được tham chiếu phải đã có và đứng TRƯỚC câu đang sửa.</summary>
    public void ShowIf(string field, JsonElement value, IReadOnlyCollection<SurveyQuestion> existing, int sortOrder, string? ownCode)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("all", out var all) || all.ValueKind != JsonValueKind.Array)
        {
            Add(field, "Phải có dạng {\"all\":[...]}.");
            return;
        }

        foreach (var condition in all.EnumerateArray())
        {
            var question = condition.ValueKind == JsonValueKind.Object ? StringProp(condition, "question") : null;
            var target = existing.FirstOrDefault(q => q.Code == question);
            if (target is null || target.Code == ownCode)
            {
                Add(field, $"Câu hỏi được tham chiếu '{question}' không tồn tại.");
            }
            else if (target.SortOrder >= sortOrder)
            {
                Add(field, $"Chỉ được điều kiện theo câu đứng TRƯỚC; '{question}' đứng sau hoặc cùng vị trí.");
            }

            var hasIn = condition.TryGetProperty("in", out var inList);
            var hasNotIn = condition.TryGetProperty("not_in", out var notInList);
            if (hasIn == hasNotIn)
            {
                Add(field, "Mỗi điều kiện phải có đúng một trong in hoặc not_in.");
            }
            else if ((hasIn ? inList : notInList) is not { ValueKind: JsonValueKind.Array } list
                     || list.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
            {
                Add(field, "in/not_in phải là mảng mã đáp án.");
            }
        }
    }

    public const int MaxAllocationGroups = 8;
    public const int MaxAllocationLabelLength = 40;

    /// <summary>
    /// Bảng phân bổ của kế hoạch tháng: mảng ≤ 8 nhóm <c>{key, label, kind, categories}</c>. <c>key</c> khác null phải là tham số percent của chính phương pháp;
    /// nhóm <c>save</c> bắt buộc có <c>key</c> và không chứa danh mục; một danh mục thuộc tối đa một nhóm; tối đa một nhóm <c>spend</c> không có <c>key</c>;
    /// danh mục phải là danh mục hệ thống đang bật.
    /// </summary>
    public void Allocation(string field, JsonElement value, JsonElement paramDefs, IReadOnlySet<string> systemSlugs)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(field, "Phải là một mảng.");
            return;
        }

        var groups = value.EnumerateArray().ToList();
        if (groups.Count > MaxAllocationGroups)
        {
            Add(field, $"Tối đa {MaxAllocationGroups} nhóm.");
        }

        var types = ParamTypeByKey(paramDefs);
        var seen = new HashSet<string>();
        var restGroups = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var at = $"{field}[{i}]";
            if (group.ValueKind != JsonValueKind.Object)
            {
                Add(at, "Mỗi nhóm phải là một object.");
                continue;
            }

            var kind = StringProp(group, "kind");
            if (kind is null || !AllocationKinds.All.Contains(kind))
            {
                Add($"{at}.kind", $"Chỉ nhận: {string.Join(", ", AllocationKinds.All)}.");
            }

            var label = StringProp(group, "label");
            if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > MaxAllocationLabelLength)
            {
                Add($"{at}.label", $"Tên nhóm từ 1 đến {MaxAllocationLabelLength} ký tự.");
            }

            string? key = null;
            if (group.TryGetProperty("key", out var keyElement) && keyElement.ValueKind != JsonValueKind.Null)
            {
                key = keyElement.ValueKind == JsonValueKind.String ? keyElement.GetString() : null;
                if (key is null || types.GetValueOrDefault(key) != FinanceParamTypes.Percent)
                {
                    Add($"{at}.key", "Phải là mã một tham số kiểu % của chính cách quản lý này.");
                }
            }
            else if (kind == AllocationKinds.Save)
            {
                Add($"{at}.key", "Nhóm để dành phải gắn với một tham số %.");
            }
            else if (kind == AllocationKinds.Spend)
            {
                restGroups++;
            }

            if (!group.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            {
                Add($"{at}.categories", "Phải là mảng mã danh mục.");
                continue;
            }

            var slugs = categories.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() : null).ToList();
            if (kind == AllocationKinds.Save && slugs.Count > 0)
            {
                Add($"{at}.categories", "Nhóm để dành không chứa danh mục chi.");
            }

            foreach (var slug in slugs)
            {
                if (slug is null)
                {
                    Add($"{at}.categories", "Mã danh mục phải là chuỗi.");
                }
                else if (!seen.Add(slug))
                {
                    Add($"{at}.categories", $"Danh mục '{slug}' đã thuộc một nhóm khác.");
                }
                else if (!systemSlugs.Contains(slug))
                {
                    Add(field, $"Danh mục '{slug}' không tồn tại hoặc đã tắt (chỉ dùng được mã của danh mục hệ thống).");
                }
            }
        }

        if (restGroups > 1)
        {
            Add(field, "Chỉ được một nhóm chi \"phần còn lại\" (không gắn tham số %).");
        }
    }

    /// <summary>Các trường kế hoạch của một câu khảo sát: loại câu, vai trò, danh mục và các đáp án theo đúng loại đó.</summary>
    public void SurveyPlanFields(
        string answerType, string? planRole, string? categorySlug, IReadOnlyList<(int? Min, int? Max)> activeShares, IReadOnlySet<string> systemSlugs)
    {
        if (!SurveyAnswerTypes.All.Contains(answerType))
        {
            Add("answerType", $"Chỉ nhận: {string.Join(", ", SurveyAnswerTypes.All)}.");
        }

        if (planRole is not null && !SurveyPlanRoles.All.Contains(planRole))
        {
            Add("planRole", $"Chỉ nhận: {string.Join(", ", SurveyPlanRoles.All)} hoặc để trống.");
        }

        if (planRole is SurveyPlanRoles.Income or SurveyPlanRoles.SavingsBalance && answerType != SurveyAnswerTypes.Amount)
        {
            Add("answerType", "Câu thu nhập và tiền tiết kiệm phải là câu nhập số tiền (amount).");
        }

        if (categorySlug is not null && !systemSlugs.Contains(categorySlug))
        {
            Add("categorySlug", $"Danh mục '{categorySlug}' không tồn tại hoặc đã tắt.");
        }

        if (planRole == SurveyPlanRoles.Flexible)
        {
            if (answerType != SurveyAnswerTypes.Choice)
            {
                Add("answerType", "Câu chi linh hoạt là câu chọn khoảng %.");
            }

            if (categorySlug is null)
            {
                Add("categorySlug", "Câu chi linh hoạt phải gắn một danh mục hệ thống.");
            }

            for (var i = 0; i < activeShares.Count; i++)
            {
                ShareRange($"options[{i}]", activeShares[i].Min, activeShares[i].Max);
            }
        }
    }

    /// <summary>Khoảng % của một đáp án câu chi linh hoạt: 0 ≤ min ≤ 100, max null hoặc min ≤ max ≤ 100.</summary>
    public void ShareRange(string field, int? min, int? max)
    {
        if (min is not (>= 0 and <= 100))
        {
            Add($"{field}.shareMinPct", "Câu chi linh hoạt cần % tối thiểu từ 0 đến 100.");
        }
        else if (max is not null && (max < min || max > 100))
        {
            Add($"{field}.shareMaxPct", "% tối đa phải từ % tối thiểu đến 100, hoặc để trống (\"từ mức này trở lên\").");
        }
    }

    public void Add(string field, string message) => _failures.Add(new ValidationFailure(field, message));

    private static string? StringProp(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IntProp(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out value);
    }
}
