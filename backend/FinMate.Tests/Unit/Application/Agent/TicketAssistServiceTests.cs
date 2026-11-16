using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Trợ lý ticket chỉ gợi ý: ghi chú nội bộ không bao giờ đến mô hình, nội dung bị cắt theo hợp đồng, LLM sập thì nhân sự vẫn làm việc bình thường.</summary>
public class TicketAssistServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid TicketId = Guid.NewGuid();

    private static OpsMessageDto Msg(string kind, string body, int minute)
        => new(Guid.NewGuid(), kind, null, null, body, Now.AddMinutes(minute), []);

    private static OpsTicketDetailDto Detail(string subject, params OpsMessageDto[] messages)
        => new(new OpsTicketDto(TicketId, Guid.NewGuid(), null, null, null, null, "billing", "normal", "open", subject, Now, Now, null, false, false, null, Now, Now, null, null), messages);

    private static (TicketAssistService Service, Mock<IAgentAiClient> Ai) Make(OpsTicketDetailDto detail)
    {
        var tickets = new Mock<IOpsTicketService>();
        tickets.Setup(t => t.GetAsync(Actor, TicketId, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        var ai = new Mock<IAgentAiClient>();
        return (new TicketAssistService(tickets.Object, ai.Object), ai);
    }

    [Fact]
    public async Task internal_notes_are_never_sent_and_staff_messages_become_the_staff_role()
    {
        var (service, ai) = Make(Detail("Trừ tiền sai",
            Msg("Customer", "Tôi bị trừ hai lần", 0), Msg("InternalNote", "khách này hay khiếu nại", 1), Msg("Staff", "Chúng tôi đang kiểm tra", 2), Msg("Customer", "Bao giờ xong?", 3)));
        IReadOnlyList<TicketAssistMessage>? sent = null;
        string? sentBody = null;
        ai.Setup(a => a.TicketAssistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TicketAssistMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<TicketAssistMessage>, CancellationToken>((_, b, h, _) => { sentBody = b; sent = h; })
            .ReturnsAsync(new TicketAssistResult("Khách bị trừ hai lần", "Xin lỗi, đang xử lý", true, null));

        var dto = await service.AssistAsync(Actor, TicketId);

        dto.Should().Be(new TicketAssistDto(true, "Khách bị trừ hai lần", "Xin lỗi, đang xử lý", true));
        sentBody.Should().Be("Tôi bị trừ hai lần");
        sent!.Select(m => (m.Role, m.Text)).Should().Equal(("staff", "Chúng tôi đang kiểm tra"), ("user", "Bao giờ xong?"));
    }

    [Fact]
    public async Task long_text_is_clipped_to_the_contract_limits()
    {
        var (service, ai) = Make(Detail(new string('s', 500), Msg("Customer", new string('x', 5000), 0)));
        string? subject = null;
        string? body = null;
        ai.Setup(a => a.TicketAssistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TicketAssistMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<TicketAssistMessage>, CancellationToken>((s, b, _, _) => { subject = s; body = b; })
            .ReturnsAsync(new TicketAssistResult("ok", null, true, null));

        await service.AssistAsync(Actor, TicketId);

        subject!.Length.Should().Be(120);
        body!.Length.Should().Be(2000);
    }

    [Fact]
    public async Task an_unavailable_ai_service_answers_available_false_instead_of_failing()
    {
        var (service, ai) = Make(Detail("x", Msg("Customer", "hi", 0)));
        ai.Setup(a => a.TicketAssistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TicketAssistMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));

        (await service.AssistAsync(Actor, TicketId)).Should().Be(new TicketAssistDto(false, null, null, false));
    }
}
