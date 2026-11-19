using FinMate.Application.Subscriptions;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Infrastructure.Persistence.Seed;

public static partial class DashboardDevelopmentSeeder
{
    private const string Cpi2024 = "https://www.nso.gov.vn/du-lieu-va-so-lieu-thong-ke/2025/01/thong-cao-bao-chi-ve-tinh-hinh-gia-thang-muoi-hai-quy-iv-va-nam-2024/";
    private const string Cpi2025 = "https://www.nso.gov.vn/du-lieu-va-so-lieu-thong-ke/2026/01/thong-cao-bao-chi-ve-tinh-hinh-gia-thang-muoi-hai-quy-iv-va-nam-2025/";
    private const string FuelSource = "https://phuclocninh.vn/phuclocninhpetrol-dieu-chinh-gia-xang-dau-tu-15-gio-00-phut-ngay-19-11-2026/";

    private static void AddPublicReferences(FinMateDbContext db, Guid admin, DateTimeOffset now)
    {
        (string Key, int Bps, int Year, string Source)[] facts = [
            ("cpi_average_2024", 363, 2024, Cpi2024), ("core_inflation_2024", 271, 2024, Cpi2024),
            ("cpi_average_2025", 331, 2025, Cpi2025), ("core_inflation_2025", 321, 2025, Cpi2025),
            ("food_services_cpi_2025", 327, 2025, Cpi2025), ("housing_utilities_cpi_2025", 608, 2025, Cpi2025) ];
        foreach (var fact in facts) db.EconomicAssumptions.Add(new EconomicAssumption {
            Id = Id($"assumption:{fact.Key}"), Key = fact.Key, RateBps = fact.Bps, Unit = "pct_per_year", Version = 1,
            SourceName = $"Cục Thống kê — số liệu năm {fact.Year}", SourceUrl = fact.Source,
            AsOf = new DateTimeOffset(fact.Year, 12, 31, 0, 0, 0, TimeSpan.FromHours(7)),
            Status = AssumptionStatus.Draft, CreatedBy = admin, CreatedAt = now });
        AddReferenceSource(db, admin, "cpi2024", "Cục Thống kê — CPI năm 2024", Cpi2024, now);
        AddReferenceSource(db, admin, "cpi2025", "Cục Thống kê — CPI năm 2025", Cpi2025, now);
        AddReferenceSource(db, admin, "fuel", "Phúc Lộc Ninh Petrol — kỳ điều chỉnh 19/11/2026", FuelSource, now);
        db.FuelPrices.Add(new FuelPrice { Id = Id("fuel:2026-11-19:e10"), FuelKey = "e10_ron95_3",
            Region1PriceCents = 19_100, Region2PriceCents = 19_480,
            FirstSeenAt = new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.FromHours(7)),
            Origin = FuelPriceOrigin.Manual, CreatedByUserId = admin, AlertsProcessedAt = now,
            Note = $"Nguồn công bố: {FuelSource} Giá VND/lít của kỳ 19/11/2026; nhập tham chiếu thủ công, không phải kết quả crawler." });
    }

    private static void AddReferenceSource(FinMateDbContext db, Guid admin, string key, string name, string url, DateTimeOffset now)
    {
        db.MarketSources.Add(new MarketSource { Id = Id($"source:{key}"), Name = name, Url = url, Kind = MarketSourceKind.Page,
            AiAllowed = false, IsActive = false, CreatedByUserId = admin, CreatedAt = now, UpdatedAt = now });
        // A curated link is not a completed crawl or an AI extraction. Keep acquisition status honest.
        db.MarketDocuments.Add(new MarketDocument { Id = Id($"document:{key}"), SourceId = Id($"source:{key}"),
            Url = url, Title = name, Status = MarketDocumentStatus.NoContent, TextLength = 0, FetchedAt = now });
    }

    private static void AddSubscriptions(FinMateDbContext db, Dictionary<string, User> people, DateTimeOffset now)
    {
        foreach (var (key, name, price) in new[] { ("personal", "Cá nhân", 29_000L), ("plus", "Nâng cao", 59_000L) })
        {
            var plan = new SubscriptionPlan { Id = Id($"plan:{key}"), Code = $"dev_dashboard_{key}", Name = $"{Label} {name}",
                Description = "Gói mô phỏng để thử chỉnh giá và quyền lợi. Không phải gói thương mại; không dùng để thanh toán thật.",
                MonthlyPriceCents = price, YearlyPriceCents = price * 10, IsDefaultFree = false, IsActive = false,
                SortOrder = key == "personal" ? 90 : 91, CreatedAt = now.AddDays(-7), UpdatedAt = now };
            foreach (var feature in SubscriptionFeatures.All) plan.Features.Add(new SubscriptionPlanFeature {
                PlanId = plan.Id, FeatureKey = feature.Key, Enabled = true, MonthlyLimit = key == "personal" ? 100 : null });
            db.SubscriptionPlans.Add(plan);
        }
        foreach (var (key, user, status) in new[] { ("pending", "an", SubscriptionOrderStatus.Pending),
            ("expired", "binh", SubscriptionOrderStatus.Expired), ("cancelled", "chi", SubscriptionOrderStatus.Cancelled) })
        {
            db.SubscriptionOrders.Add(new SubscriptionOrder { Id = Id($"order:{key}"), InvoiceNumber = $"DEV-DASH-{key.ToUpperInvariant()}-V1",
                UserId = people[user].Id, PlanId = Id("plan:personal"), PlanCode = "dev_dashboard_personal", PlanName = $"{Label} Cá nhân",
                FeaturesJson = System.Text.Json.JsonSerializer.Serialize(SubscriptionFeatures.All.Select(feature => new FeatureGrant(feature.Key, true, 100)), JsonOptions),
                Cycle = SubscriptionCycle.Monthly, AmountCents = 29_000, Status = status, CreatedAt = now.AddDays(-2),
                ExpiresAt = status == SubscriptionOrderStatus.Pending ? now.AddDays(1) : now.AddDays(-1),
                NextCheckAt = now.AddDays(2) });
        }
    }

    private static void AddForecastFeedback(FinMateDbContext db, Dictionary<string, User> people, DateTimeOffset now)
    {
        db.ForecastFeedback.Add(new ForecastFeedback { Id = Id("forecast-feedback:an"), UserId = people["an"].Id,
            Reason = ForecastFeedbackReasons.MissingBill, Status = ForecastFeedbackStatuses.New, CreatedAt = now.AddHours(-4),
            Note = $"{Label} Chưa nhập tiền Internet nên người dùng mô phỏng báo thiếu khoản chi. Không gắn số dự báo được bịa ra." });
        db.ForecastFeedback.Add(new ForecastFeedback { Id = Id("forecast-feedback:binh"), UserId = people["binh"].Id,
            Reason = ForecastFeedbackReasons.UnusualSpending, Status = ForecastFeedbackStatuses.Labeled,
            Cause = ForecastFeedbackCauses.OneOff, LabeledByUserId = people["staff"].Id, LabeledAt = now.AddHours(-2),
            Note = $"{Label} Khoản mua sắm phát sinh ngoài kế hoạch; tình huống phục vụ thử gán nguyên nhân.", CreatedAt = now.AddDays(-1) });
    }
}
