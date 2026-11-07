using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;
using S = FinMate.Domain.Entities.SupportTicketStatus;

namespace FinMate.Tests.Unit.Application.Support;

public class TicketStateTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();

    private sealed class FakeRepo : ISupportTicketRepository
    {
        public List<SupportTicket> All { get; } = [];

        public Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(All.FirstOrDefault(t => t.Id == id));

        public Task<SupportTicket?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default) => Task.FromResult(All.FirstOrDefault(t => t.Id == id && t.UserId == userId));

        public Task<(int Total, List<SupportTicket> Items)> ListForUserAsync(Guid userId, S? status, int limit, int offset, CancellationToken ct = default)
        {
            var q = All.Where(t => t.UserId == userId && (status == null || t.Status == status)).ToList();
            return Task.FromResult((q.Count, q.Skip(offset).Take(limit).ToList()));
        }

        public Task<(int Total, List<SupportTicket> Items)> ListAllAsync(S? status, string? search, int limit, int offset, CancellationToken ct = default)
        {
            var q = All.Where(t => status == null || t.Status == status).ToList();
            return Task.FromResult((q.Count, q.Skip(offset).Take(limit).ToList()));
        }

        public Task<int> CountOpenForUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(All.Count(t => t.UserId == userId && t.Status is not (S.Resolved or S.Rejected)));

        public Task AddAsync(SupportTicket ticket, CancellationToken ct = default)
        {
            All.Add(ticket);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SupportTicket ticket, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAudit : IAuditLogService
    {
        public List<(string Event, string Metadata)> Rows { get; } = [];

        public Task LogAsync(string eventType, Guid? userId, string? ipAddress = null, object? metadata = null, CancellationToken ct = default)
        {
            Rows.Add((eventType, System.Text.Json.JsonSerializer.Serialize(metadata)));
            return Task.CompletedTask;
        }
    }

    private static (SupportTicketService Service, FakeRepo Repo, FakeAudit Audit, Mock<IAgentAiClient> Ai) Build()
    {
        var repo = new FakeRepo();
        var audit = new FakeAudit();
        var ai = new Mock<IAgentAiClient>();
        return (new SupportTicketService(repo, audit, ai.Object), repo, audit, ai);
    }

    private static async Task<SupportTicketDto> InStateAsync(SupportTicketService service, FakeRepo repo, S state)
    {
        var created = await service.CreateAsync(Owner, "Bị trừ tiền hai lần", "Mình bị trừ 150.000 đồng hai lần.", null);
        repo.All.Single(t => t.Id == created.Id).Status = state;
        return created;
    }

    [Theory]
    [InlineData(S.Open, S.Resolved)]
    [InlineData(S.Open, S.Responded)]
    [InlineData(S.Triaged, S.Resolved)]
    [InlineData(S.Responded, S.Escalated)]
    [InlineData(S.Resolved, S.Triaged)]
    [InlineData(S.Escalated, S.Resolved)]
    public async Task illegal_transition_is_rejected(S from, S to)
    {
        var (service, repo, audit, _) = Build();
        var ticket = await InStateAsync(service, repo, from);
        var before = audit.Rows.Count;

        var act = () => service.StaffTransitionAsync(Staff, UserRole.Admin, ticket.Id, to, "đã xử lý", "ghi chú", null);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be("illegal_ticket_transition");
        repo.All.Single().Status.Should().Be(from);
        audit.Rows.Should().HaveCount(before);
    }

    [Fact]
    public async Task the_normal_flow_runs_open_triaged_responded_resolved_and_every_step_is_audited()
    {
        var (service, repo, audit, _) = Build();
        var ticket = await service.CreateAsync(Owner, "Hỏi về số dư", "Số dư hiển thị sai.", null);

        await service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.Triaged, null, null, null);
        await service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.Responded, "Bạn thử đồng bộ lại nhé.", null, null);
        await service.OwnerTransitionAsync(Owner, ticket.Id, S.Resolved, null);

        repo.All.Single().Status.Should().Be(S.Resolved);
        repo.All.Single().Reply.Should().Be("Bạn thử đồng bộ lại nhé.");
        audit.Rows.Select(r => r.Event).Should().Equal(AuditEvents.SupportTicketCreated, AuditEvents.SupportTicketTransitioned, AuditEvents.SupportTicketTransitioned, AuditEvents.SupportTicketTransitioned);
        audit.Rows.Skip(1).Select(r => r.Metadata).Should().OnlyContain(m => m.Contains("ticketId") && m.Contains("from") && m.Contains("to") && m.Contains("actor"));
        audit.Rows.Select(r => r.Metadata).Should().NotContain(m => m.Contains("Bạn thử") || m.Contains("150.000") || m.Contains("số dư", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task escalation_requires_manager_to_resolve()
    {
        var (service, repo, _, _) = Build();
        var ticket = await InStateAsync(service, repo, S.Triaged);
        await service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.Escalated, null, null, null);

        var supportTakesOver = () => service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.ManagerReview, null, null, null);
        await supportTakesOver.Should().ThrowAsync<ForbiddenException>();

        await service.StaffTransitionAsync(Staff, UserRole.Manager, ticket.Id, S.ManagerReview, null, null, null);
        var supportDecides = () => service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.Resolved, null, "đã xử lý xong", null);
        await supportDecides.Should().ThrowAsync<ForbiddenException>();
        repo.All.Single().Status.Should().Be(S.ManagerReview);

        var done = await service.StaffTransitionAsync(Staff, UserRole.Manager, ticket.Id, S.Rejected, null, "Không đủ căn cứ để hoàn tiền.", null);
        done.Status.Should().Be("Rejected");
        done.ResolutionNote.Should().Be("Không đủ căn cứ để hoàn tiền.");
    }

    [Fact]
    public async Task a_final_decision_needs_a_note_and_a_reply_needs_text()
    {
        var (service, repo, _, _) = Build();
        var review = await InStateAsync(service, repo, S.ManagerReview);
        var triaged = await InStateAsync(service, repo, S.Triaged);

        var noNote = () => service.StaffTransitionAsync(Staff, UserRole.Manager, review.Id, S.Resolved, null, "  ", null);
        var noReply = () => service.StaffTransitionAsync(Staff, UserRole.SupportStaff, triaged.Id, S.Responded, " ", null, null);

        (await noNote.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_ticket_note");
        (await noReply.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_ticket_reply");
        repo.All.Select(t => t.Status).Should().BeEquivalentTo([S.ManagerReview, S.Triaged]);
    }

    [Theory]
    [InlineData(S.Responded)]
    [InlineData(S.Resolved)]
    [InlineData(S.Rejected)]
    public async Task the_owner_can_object_to_a_result_and_the_ticket_reopens(S state)
    {
        var (service, repo, _, _) = Build();
        var ticket = await InStateAsync(service, repo, state);

        var reopened = await service.OwnerTransitionAsync(Owner, ticket.Id, S.Reopened, null);

        reopened.Status.Should().Be("Reopened");
        (await service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.Triaged, null, null, null)).Status.Should().Be("Triaged");
    }

    [Fact]
    public async Task the_owner_cannot_do_staff_steps_and_staff_cannot_do_owner_steps()
    {
        var (service, repo, _, _) = Build();
        var open = await InStateAsync(service, repo, S.Open);
        var responded = await InStateAsync(service, repo, S.Responded);

        var ownerTriages = () => service.OwnerTransitionAsync(Owner, open.Id, S.Triaged, null);
        var staffReopens = () => service.StaffTransitionAsync(Staff, UserRole.Admin, responded.Id, S.Reopened, null, null, null);

        await ownerTriages.Should().ThrowAsync<ForbiddenException>();
        await staffReopens.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.ContentStaff)]
    public async Task roles_outside_support_cannot_move_a_ticket(UserRole role)
    {
        var (service, repo, _, _) = Build();
        var ticket = await InStateAsync(service, repo, S.Open);

        var act = () => service.StaffTransitionAsync(Staff, role, ticket.Id, S.Triaged, null, null, null);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task user_sees_only_own_tickets()
    {
        var (service, _, _, _) = Build();
        var mine = await service.CreateAsync(Owner, "của tôi", "nội dung", null);
        var theirs = await service.CreateAsync(Guid.NewGuid(), "của người khác", "nội dung", null);

        (await service.ListMineAsync(Owner, null, 20, 0)).Items.Select(i => i.Id).Should().Equal(mine.Id);
        await ((Func<Task>)(() => service.GetMineAsync(Owner, theirs.Id))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => service.OwnerTransitionAsync(Owner, theirs.Id, S.Reopened, null))).Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("", "nội dung", "invalid_ticket_subject")]
    [InlineData("tiêu đề", "   ", "invalid_ticket_body")]
    public async Task invalid_input_is_rejected(string subject, string body, string code)
    {
        var (service, repo, _, _) = Build();

        var act = () => service.CreateAsync(Owner, subject, body, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(code);
        repo.All.Should().BeEmpty();
    }

    [Fact]
    public async Task too_many_open_tickets_are_refused_but_closed_ones_do_not_count()
    {
        var (service, repo, _, _) = Build();
        for (var i = 0; i < SupportTicketService.MaxOpenPerUser; i++)
        {
            await service.CreateAsync(Owner, $"t{i}", "nội dung", null);
        }

        var refused = () => service.CreateAsync(Owner, "thêm", "nội dung", null);
        await refused.Should().ThrowAsync<BusinessRuleException>();

        repo.All[0].Status = S.Resolved;
        (await service.CreateAsync(Owner, "thêm", "nội dung", null)).Status.Should().Be("Open");
    }

    [Fact]
    public async Task suggested_reply_is_not_sent_automatically()
    {
        var (service, repo, audit, ai) = Build();
        var ticket = await InStateAsync(service, repo, S.Triaged);
        ai.Setup(a => a.TicketAssistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TicketAssistMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketAssistResult("Tóm tắt", "Bản nháp trả lời", true, null));
        var rowsBefore = audit.Rows.Count;

        var assist = await service.AssistAsync(ticket.Id);

        assist.Should().Be(new TicketAssistDto(true, "Tóm tắt", "Bản nháp trả lời", true));
        var stored = repo.All.Single();
        stored.Status.Should().Be(S.Triaged);
        stored.Reply.Should().BeNull();
        audit.Rows.Should().HaveCount(rowsBefore);
    }

    [Fact]
    public async Task assist_failure_does_not_block_ticket()
    {
        var (service, repo, _, ai) = Build();
        var ticket = await InStateAsync(service, repo, S.Triaged);
        ai.Setup(a => a.TicketAssistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TicketAssistMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("sập"));

        var assist = await service.AssistAsync(ticket.Id);
        var replied = await service.StaffTransitionAsync(Staff, UserRole.SupportStaff, ticket.Id, S.Responded, "Mình đã kiểm tra giúp bạn.", null, null);

        assist.Available.Should().BeFalse();
        replied.Status.Should().Be("Responded");
    }

    [Fact]
    public async Task assist_sends_the_ticket_text_and_the_staff_reply_as_history_and_no_identity()
    {
        var (service, repo, _, ai) = Build();
        var ticket = await InStateAsync(service, repo, S.Reopened);
        repo.All.Single().Reply = "Bạn gửi mã đơn nhé.";
        IReadOnlyList<TicketAssistMessage>? sent = null;
        ai.Setup(a => a.TicketAssistAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<TicketAssistMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyList<TicketAssistMessage>, CancellationToken>((_, _, h, _) => sent = h)
            .ReturnsAsync(new TicketAssistResult("s", null, false, null));

        var assist = await service.AssistAsync(ticket.Id);

        sent.Should().ContainSingle().Which.Should().Be(new TicketAssistMessage("staff", "Bạn gửi mã đơn nhé."));
        assist.Should().Be(new TicketAssistDto(true, "s", null, false));
    }
}
