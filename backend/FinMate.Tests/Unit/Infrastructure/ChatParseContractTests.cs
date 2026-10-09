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

    [Fact]
    public void TheRequestIsSentWithTheNamesTheAiServiceExpects()
    {
        var json = JsonSerializer.Serialize(new ChatParseApiRequest("đi ăn 200k", new string('a', 64)), Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("text").GetString().Should().Be("đi ăn 200k");
        doc.RootElement.GetProperty("user_id_hash").GetString().Should().HaveLength(64);
    }
}
