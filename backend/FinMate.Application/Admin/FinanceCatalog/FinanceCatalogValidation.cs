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

    public void Add(string field, string message) => _failures.Add(new ValidationFailure(field, message));

    private static string? StringProp(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IntProp(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out value);
    }
}
