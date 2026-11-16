using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Bộ nhớ dài hạn do chính người dùng quản lý: khóa trong danh sách cho phép, giá trị ngắn, xóa hết thì cả ngữ cảnh ngắn hạn cũng mất.</summary>
public class AgentMemoryServiceTests
{
    private sealed class MapCache : ICacheService
    {
        public Dictionary<string, string> Items { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(Items.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        {
            Items[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            Items.Remove(key);
            return Task.CompletedTask;
        }
    }

    private readonly Mock<IAgentAiClient> _ai = new();
    private readonly MapCache _cache = new();
    private readonly Guid _me = Guid.NewGuid();

    private AgentMemoryService Service() => new(_ai.Object, new ConversationStore(_cache));

    [Theory]
    [InlineData("unknown_key")]
    [InlineData("")]
    [InlineData("PREFERRED_METHOD")]
    public async Task put_rejects_a_key_outside_the_allowed_list(string key)
    {
        var act = () => Service().PutAsync(_me, key, "giá trị");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_memory_key");
        _ai.Verify(a => a.PutMemoryAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task put_rejects_a_blank_value(string value)
        => (await ((Func<Task>)(async () => await Service().PutAsync(_me, "preferred_method", value))).Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_memory_value");

    [Fact]
    public async Task put_rejects_a_value_over_the_limit_and_accepts_exactly_the_limit()
    {
        var tooLong = new string('x', AgentLimits.MaxMemoryValueLength + 1);
        (await ((Func<Task>)(async () => await Service().PutAsync(_me, "preferred_method", tooLong))).Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_memory_value");

        await Service().PutAsync(_me, "preferred_method", new string('x', AgentLimits.MaxMemoryValueLength));

        _ai.Verify(a => a.PutMemoryAsync(_me, "preferred_method", new string('x', AgentLimits.MaxMemoryValueLength), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task put_trims_the_value_and_forwards_it_for_this_user_only()
    {
        await Service().PutAsync(_me, "communication_tone", "  thân thiện  ");

        _ai.Verify(a => a.PutMemoryAsync(_me, "communication_tone", "thân thiện", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task deleting_everything_also_clears_the_short_term_conversation()
    {
        var store = new ConversationStore(_cache);
        await store.SaveAsync(_me, new ConversationState("forecast_goal", new Dictionary<string, string> { ["price_vnd"] = "600000000" }, 1, DateTimeOffset.UtcNow));
        _ai.Setup(a => a.DeleteMemoryAsync(_me, null, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var deleted = await Service().DeleteAsync(_me, null);

        deleted.Should().Be(2);
        (await store.GetAsync(_me)).Should().BeNull();
    }

    [Fact]
    public async Task deleting_one_key_keeps_the_conversation_and_validates_the_key()
    {
        var store = new ConversationStore(_cache);
        await store.SaveAsync(_me, new ConversationState(null, [], 1, DateTimeOffset.UtcNow));
        _ai.Setup(a => a.DeleteMemoryAsync(_me, "preferred_method", It.IsAny<CancellationToken>())).ReturnsAsync(1);

        (await Service().DeleteAsync(_me, "preferred_method")).Should().Be(1);

        (await store.GetAsync(_me)).Should().NotBeNull();
        (await ((Func<Task>)(async () => await Service().DeleteAsync(_me, "nope"))).Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_memory_key");
    }

    [Fact]
    public async Task list_returns_what_the_ai_service_stores_for_this_user()
    {
        IReadOnlyList<AgentMemoryEntry> stored = [new AgentMemoryEntry("preferred_method", "phong bì", DateTimeOffset.UtcNow)];
        _ai.Setup(a => a.GetMemoryAsync(_me, It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        (await Service().ListAsync(_me)).Should().BeSameAs(stored);
    }

    [Fact]
    public void the_conversation_key_is_distinct_per_user()
        => CacheKeys.AgentConversation(_me).Should().NotBe(CacheKeys.AgentConversation(Guid.NewGuid()));
}
