using System.Text.Json;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// JSON mẫu dưới đây KHÔNG tự viết tay: sinh bằng đúng <c>ChatParseResponse.model_dump_json()</c> của AI Service
/// (ai-service/app/schemas/chat.py). Tên trường lệch một chữ thì System.Text.Json im lặng để null/0 và chat hỏng
/// mà không có lỗi nào, nên contract được ghim ở đây.
/// </summary>
public class ChatParseContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private const string ExpenseJson =
        """{"intent":"add_expense","reply_text":null,"entry":{"kind":"expense","amount_cents":200000,"description":"đi ăn","merchant":null,"category_slug":"food","income_type":null,"auto_save_ok":true,"to_others":false},"query":null,"nlu":{"source":"rasa","intent":"add_expense","confidence":0.97},"unclear_reason":null,"processing_ms":12}""";

    private const string QueryJson =
        """{"intent":"ask_spending","reply_text":null,"entry":null,"query":{"metric":"expense","period_key":"thang nay","period_label":"tháng này","start":"2026-10-01T00:00:00+07:00","end":"2026-11-01T00:00:00+07:00","category_slug":"food","income_type":null},"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":3}""";

    [Fact]
    public void AnEntryFromTheAiServiceDeserialisesIntoEveryField()
    {
        var response = JsonSerializer.Deserialize<ChatParseApiResponse>(ExpenseJson, Options)!;

        response.Intent.Should().Be("add_expense");
        response.Query.Should().BeNull();
        response.ProcessingMs.Should().Be(12);
        var entry = response.Entry!;
        entry.Kind.Should().Be("expense");
        entry.AmountCents.Should().Be(200_000);
        entry.Description.Should().Be("đi ăn");
        entry.CategorySlug.Should().Be("food");
        entry.AutoSaveOk.Should().BeTrue();
        entry.ToOthers.Should().BeFalse();
    }

    [Fact]
    public void AQueryFromTheAiServiceKeepsItsPeriodInVietnamTime()
    {
        var response = JsonSerializer.Deserialize<ChatParseApiResponse>(QueryJson, Options)!;

        response.Entry.Should().BeNull();
        var query = response.Query!;
        query.Metric.Should().Be("expense");
        query.PeriodLabel.Should().Be("tháng này");
        query.CategorySlug.Should().Be("food");
        query.Start.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(7)));
        query.Start.Offset.Should().Be(TimeSpan.FromHours(7));
        query.End.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7)));
    }

    // Sinh bằng ChatParseResponse.model_dump_json() cho "tháng này tôi đã tiêu bao nhiêu tiền cho giải trí và mua sắm" (2026-10-17): có category_slugs.
    private const string TwoCategoryQueryJson =
        """{"intent":"ask_spending","reply_text":null,"entry":null,"entries":[],"query":{"metric":"expense","period_key":"thang nay","period_label":"tháng này","start":"2026-10-01T00:00:00+07:00","end":"2026-11-01T00:00:00+07:00","category_slug":"entertainment","category_slugs":["entertainment","shopping"],"income_type":null},"total_amount":null,"total_mismatch":false,"skipped":[],"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":3}""";

    [Fact]
    public void AQueryAboutTwoCategoriesKeepsBothAndTheOldShapeStillDeserialises()
    {
        var two = JsonSerializer.Deserialize<ChatParseApiResponse>(TwoCategoryQueryJson, Options)!.Query!;
        two.CategorySlugs.Should().Equal("entertainment", "shopping");
        two.CategorySlug.Should().Be("entertainment");

        var old = JsonSerializer.Deserialize<ChatParseApiResponse>(QueryJson, Options)!.Query!;  // AI Service cũ: không có category_slugs
        old.CategorySlugs.Should().BeNull();
        old.CategorySlug.Should().Be("food");
    }

    // Sinh bằng ChatParseResponse.model_dump_json() cho "đặt mục tiêu vào 2 tháng sau tôi sẽ mua được xe máy khoảng 50 triệu" (hôm nay 17/10/2026).
    private const string GoalJson =
        """{"intent":"create_goal","reply_text":"Mình hiểu mục tiêu của bạn như thế này. Bạn kiểm tra rồi bấm Tạo mục tiêu nhé.","entry":null,"entries":[],"query":null,"total_amount":null,"total_mismatch":false,"skipped":[],"goal":{"name":"Mua xe máy","name_is_default":false,"target_cents":50000000,"deadline":"2026-12-17T23:59:59+07:00","deadline_text":"2 tháng sau"},"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":4}""";

    [Fact]
    public void AGoalDraftDeserialisesWithItsDeadlineInVietnamTimeAndOldResponsesHaveNone()
    {
        var goal = JsonSerializer.Deserialize<ChatParseApiResponse>(GoalJson, Options)!.Goal!;
        goal.Name.Should().Be("Mua xe máy");
        goal.NameIsDefault.Should().BeFalse();
        goal.TargetCents.Should().Be(50_000_000);
        goal.DeadlineText.Should().Be("2 tháng sau");
        goal.Deadline.Should().Be(new DateTimeOffset(2026, 12, 17, 23, 59, 59, TimeSpan.FromHours(7)));
        goal.Deadline!.Value.Offset.Should().Be(TimeSpan.FromHours(7));

        JsonSerializer.Deserialize<ChatParseApiResponse>(ExpenseJson, Options)!.Goal.Should().BeNull();  // AI Service cũ: không có goal
    }

    [Fact]
    public void TheRequestIsSentWithTheNamesTheAiServiceExpects()
    {
        var json = JsonSerializer.Serialize(new ChatParseApiRequest("đi ăn 200k", new string('a', 64)), Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("text").GetString().Should().Be("đi ăn 200k");
        doc.RootElement.GetProperty("user_id_hash").GetString().Should().HaveLength(64);
    }

    // Sinh bằng ChatParseResponse.model_dump_json() với câu "hôm qua ăn phở 45k, cà phê 30k" (có entries và occurred_at).
    private const string MultiJson =
        """{"intent":"add_expense","reply_text":null,"entry":{"kind":"expense","amount_cents":45000,"description":"ăn phở","merchant":null,"category_slug":"food","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":"2026-10-13T12:00:00+07:00"},"entries":[{"kind":"expense","amount_cents":45000,"description":"ăn phở","merchant":null,"category_slug":"food","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":"2026-10-13T12:00:00+07:00"},{"kind":"expense","amount_cents":30000,"description":"cà phê","merchant":null,"category_slug":"food","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":"2026-10-13T12:00:00+07:00"}],"query":null,"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":4}""";

    [Fact]
    public void EntriesAndTheDateDeserialiseAndTheOldShapeStillDoes()
    {
        var many = JsonSerializer.Deserialize<ChatParseApiResponse>(MultiJson, Options)!;
        many.Entries!.Select(e => e.AmountCents).Should().Equal(45_000, 30_000);
        many.Entry!.OccurredAt.Should().Be(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(7)));

        var old = JsonSerializer.Deserialize<ChatParseApiResponse>(ExpenseJson, Options)!;  // AI Service cũ: không có entries, occurred_at
        old.Entries.Should().BeNull();
        old.Entry!.OccurredAt.Should().BeNull();
    }

    // Sinh bằng ChatParseResponse.model_dump_json() sau khi thêm total_amount, total_mismatch, skipped, category_source (2026-10-13).
    private const string TotalMismatchJson =
        """{"intent":"add_expense","reply_text":null,"entry":{"kind":"expense","amount_cents":25000,"description":"mua bánh mì","merchant":null,"category_slug":"food","category_source":"rule","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null},"entries":[{"kind":"expense","amount_cents":25000,"description":"mua bánh mì","merchant":null,"category_slug":"food","category_source":"rule","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null},{"kind":"expense","amount_cents":35000,"description":"cà phê","merchant":null,"category_slug":"food","category_source":"rule","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null}],"query":null,"total_amount":70000,"total_mismatch":true,"skipped":[],"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":7}""";

    private const string SkippedJson =
        """{"intent":"add_expense","reply_text":null,"entry":{"kind":"expense","amount_cents":40000,"description":"ăn phở","merchant":null,"category_slug":"food","category_source":"rule","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null},"entries":[{"kind":"expense","amount_cents":40000,"description":"ăn phở","merchant":null,"category_slug":"food","category_source":"rule","income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null}],"query":null,"total_amount":null,"total_mismatch":false,"skipped":["cà phê"],"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":7}""";

    [Fact]
    public void StatedTotalAndItsMismatchFlagSurviveTheContract()
    {
        var response = JsonSerializer.Deserialize<ChatParseApiResponse>(TotalMismatchJson, Options)!;

        response.TotalAmount.Should().Be(70_000);
        response.TotalMismatch.Should().BeTrue();
        response.Entries!.Select(e => e.AmountCents).Should().Equal(25_000, 35_000);
        response.Entries!.Should().OnlyContain(e => e.CategorySource == "rule");
    }

    [Fact]
    public void SkippedClausesSurviveTheContract()
    {
        var response = JsonSerializer.Deserialize<ChatParseApiResponse>(SkippedJson, Options)!;

        response.Skipped.Should().Equal("cà phê");
        response.TotalAmount.Should().BeNull();
        response.TotalMismatch.Should().BeFalse();
    }

    // Sinh bằng ChatParseResponse.model_dump_json() cho "mua xe 2000tr và bảo hiểm 200tr tổng 2200tr": tổng 2,2 tỷ vượt int32.
    private const string BigTotalJson =
        """{"intent":"add_expense","reply_text":null,"entry":{"kind":"expense","amount_cents":2000000000,"description":"mua xe","merchant":null,"category_slug":null,"category_source":null,"income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null},"entries":[{"kind":"expense","amount_cents":2000000000,"description":"mua xe","merchant":null,"category_slug":null,"category_source":null,"income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null},{"kind":"expense","amount_cents":200000000,"description":"bảo hiểm","merchant":null,"category_slug":null,"category_source":null,"income_type":null,"auto_save_ok":false,"to_others":false,"occurred_at":null}],"query":null,"total_amount":2200000000,"total_mismatch":false,"skipped":[],"nlu":{"source":"rules","intent":null,"confidence":null},"unclear_reason":null,"processing_ms":7}""";

    [Fact]
    public void ATotalAboveInt32StillDeserialises()
    {
        var response = JsonSerializer.Deserialize<ChatParseApiResponse>(BigTotalJson, Options)!;

        response.TotalAmount.Should().Be(2_200_000_000L);
        response.Entries!.Select(e => e.AmountCents).Should().Equal(2_000_000_000L, 200_000_000L);
    }
}
