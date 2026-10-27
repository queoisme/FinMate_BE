using FinMate.Domain.Entities;

namespace FinMate.Application.FinanceMethods;

/// <summary>Gộp signals của các đáp án người dùng đã chọn thành MỘT bảng {signal: tổng trọng số}.</summary>
public static class SurveySignals
{
    /// <summary>Trần theo hợp đồng với ai-service (<c>weight</c> ∈ [1, 50]).</summary>
    public const int MaxWeight = 50;

    /// <summary>
    /// Signals của một người từ câu trả lời đã lưu (theo MÃ chữ). Đáp án không còn khớp câu hỏi nào bị bỏ qua. Truyền cả câu hỏi/đáp án đã TẮT:
    /// admin tắt một câu sau khi người dùng đã trả lời thì câu trả lời đó vẫn là thông tin thật về họ.
    /// </summary>
    public static Dictionary<string, int> FromAnswers(
        IEnumerable<UserSurveyAnswer> answers,
        IEnumerable<SurveyQuestion> questions,
        IEnumerable<SurveyOption> options)
    {
        var questionIdByCode = questions.ToDictionary(q => q.Code, q => q.Id);
        var signalsByOption = options.ToDictionary(o => (o.QuestionId, o.Code), o => o.Signals);

        return Aggregate(answers
            .Select(a => questionIdByCode.TryGetValue(a.QuestionCode, out var questionId)
                && signalsByOption.TryGetValue((questionId, a.OptionCode), out var signals)
                    ? signals
                    : null)
            .OfType<string>());
    }

    /// <param name="optionSignals">Mỗi phần tử là cột <c>survey_options.signals</c> của MỘT đáp án đã chọn.</param>
    public static Dictionary<string, int> Aggregate(IEnumerable<string> optionSignals)
    {
        var total = new Dictionary<string, int>();
        foreach (var json in optionSignals)
        {
            foreach (var (signal, weight) in FinanceJson.ReadIntMap(json))
            {
                if (weight <= 0)
                {
                    continue;
                }

                total[signal] = Math.Min(MaxWeight, total.GetValueOrDefault(signal) + weight);
            }
        }

        return total;
    }
}

/// <summary>
/// Đánh giá <c>survey_questions.show_if</c>: <c>{"all":[{"question":mã,"in":[mã đáp án]} | {"question":mã,"not_in":[mã đáp án]}]}</c>.
/// Câu chưa được trả lời không nằm trong bất kỳ danh sách nào: <c>in</c> sai, <c>not_in</c> đúng.
/// </summary>
public static class ShowIfEvaluator
{
    /// <summary>
    /// null hoặc cấu hình hỏng thì HIỆN câu hỏi: lỗi cấu hình của admin không được làm mất một câu khảo sát.
    /// </summary>
    public static bool IsVisible(string? showIfJson, IReadOnlyDictionary<string, string> answers)
    {
        if (string.IsNullOrWhiteSpace(showIfJson))
        {
            return true;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(showIfJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("all", out var all)
                || all.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return true;
            }

            foreach (var condition in all.EnumerateArray())
            {
                var question = condition.GetProperty("question").GetString();
                var answered = question is not null && answers.TryGetValue(question, out var option) ? option : null;

                if (condition.TryGetProperty("in", out var inList))
                {
                    if (answered is null || !Contains(inList, answered))
                    {
                        return false;
                    }
                }
                else if (condition.TryGetProperty("not_in", out var notInList))
                {
                    if (answered is not null && Contains(notInList, answered))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return true;
        }
    }

    private static bool Contains(System.Text.Json.JsonElement list, string optionCode)
        => list.ValueKind == System.Text.Json.JsonValueKind.Array
           && list.EnumerateArray().Any(e => e.ValueKind == System.Text.Json.JsonValueKind.String && e.GetString() == optionCode);
}
