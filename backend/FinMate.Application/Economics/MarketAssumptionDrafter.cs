using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Agent;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Economics;

/// <summary>
/// Từ giá vàng đã xác nhận, soạn bản NHÁP giả định tốc độ tăng giá (ví dụ <c>gold_price_growth</c>) để người có quyền duyệt ở trang Giả định kinh tế.
/// Khác CPI: không bao giờ tự duyệt, vì giá quá khứ của một tài sản dao động không dự báo được tương lai. Chưa đủ lịch sử thì không soạn.
/// Mỗi khóa chỉ giữ một nháp do hệ thống soạn; nháp cũ bị gỡ khi có số mới. Không bao giờ ném lỗi ra ngoài.
/// </summary>
public sealed class MarketAssumptionDrafter(
    ISystemConfigProvider config, IMarketDataRepository market, IAssumptionService assumptions, ILogger<MarketAssumptionDrafter> logger)
{
    public const string Unit = "pct_per_year";

    public static readonly Guid SystemActor = Admin.Commands.AiCategorySyncService.SystemActorId;

    /// <summary>Tốc độ một năm, điểm cơ bản: % thay đổi trong cửa sổ, quy tuyến tính ra 365 ngày. Số nguyên, làm tròn về 0.</summary>
    public static int AnnualRateBps(long past, long latest, int windowDays)
        => (int)((Int128)(latest - past) * 10_000 * 365 / ((Int128)past * windowDays));

    /// <summary>Số nháp đã soạn trong lượt này.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var settings = (await config.GetAsync(ct)).Document.MarketData;
        var drafted = 0;
        foreach (var rule in settings.AssumptionRules.Where(r => r.Enabled))
        {
            try
            {
                if (await DraftAsync(rule, settings.Gold, ct))
                {
                    drafted++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Drafting assumption {AssumptionKey} failed: {ErrorType}", rule.AssumptionKey, ex.GetType().Name);
            }
        }

        return drafted;
    }

    private async Task<bool> DraftAsync(MarketAssumptionRule rule, GoldSettings gold, CancellationToken ct)
    {
        var latest = (await market.ListLatestGoldPricesAsync(ct)).FirstOrDefault(p => p.GoldKey == rule.GoldKey);
        if (latest is null)
        {
            return false;
        }

        var past = await market.GetGoldPriceAsOfAsync(rule.GoldKey, latest.FirstSeenAt.AddDays(-rule.WindowDays), ct);
        if (past is null || past.SellCents <= 0)
        {
            return false;
        }

        var rate = AnnualRateBps(past.SellCents, latest.SellCents, rule.WindowDays);
        var existing = await assumptions.ListAsync(rule.AssumptionKey, ct);
        var approved = existing.FirstOrDefault(a => a.Status == AssumptionStatus.Approved);
        if (approved is not null && Math.Abs(rate - approved.RateBps) < rule.MinDeltaBps)
        {
            return false;
        }

        var ownDrafts = existing.Where(a => a.Status == AssumptionStatus.Draft && a.CreatedBy == SystemActor).ToList();
        if (ownDrafts.Any(d => d.RateBps == rate))
        {
            return false; // đã có nháp đúng số này đang chờ duyệt
        }

        var domain = latest.SourceDomains?.FirstOrDefault() ?? (await market.GetLatestSourceGoldPriceAsync(rule.GoldKey, ct))?.SourceDomains?.FirstOrDefault();
        if (domain is null)
        {
            return false; // giá nhập tay và chưa có nguồn nào: không có đường dẫn để người duyệt kiểm
        }

        foreach (var draft in ownDrafts)
        {
            await assumptions.RetireAsync(draft.Id, SystemActor, null, ct);
        }

        var label = gold.Rows.FirstOrDefault(r => r.Key == rule.GoldKey)?.Label ?? rule.GoldKey;
        var sourceName = $"FinMate — giá {label} (bán ra) đã xác nhận: {ForecastText.Money(past.SellCents)} ngày {past.FirstSeenAt.ToOffset(VietnamOffset):dd/MM/yyyy} → "
            + $"{ForecastText.Money(latest.SellCents)} ngày {latest.FirstSeenAt.ToOffset(VietnamOffset):dd/MM/yyyy}, quy ra mỗi năm";
        try
        {
            await assumptions.CreateDraftAsync(SystemActor, rule.AssumptionKey, rate, Unit, sourceName, $"https://{domain}/", latest.FirstSeenAt, null, ct);
        }
        catch (BusinessRuleException)
        {
            return false; // tỉ lệ ngoài khoảng giả định cho phép: thà không có nháp còn hơn một nháp vô lý
        }

        return true;
    }

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
}
