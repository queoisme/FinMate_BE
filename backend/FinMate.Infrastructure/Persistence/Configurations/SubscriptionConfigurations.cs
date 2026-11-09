using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans", t =>
        {
            t.HasCheckConstraint("chk_subscription_plans_monthly_price", "monthly_price_cents IS NULL OR monthly_price_cents > 0");
            t.HasCheckConstraint("chk_subscription_plans_yearly_price", "yearly_price_cents IS NULL OR yearly_price_cents > 0");
            t.HasCheckConstraint("chk_subscription_plans_free_has_no_price", "NOT is_default_free OR (monthly_price_cents IS NULL AND yearly_price_cents IS NULL)");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(p => p.MonthlyPriceCents).HasColumnName("monthly_price_cents");
        builder.Property(p => p.YearlyPriceCents).HasColumnName("yearly_price_cents");
        builder.Property(p => p.IsDefaultFree).HasColumnName("is_default_free").HasDefaultValue(false).IsRequired();
        builder.Property(p => p.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(p => p.SortOrder).HasColumnName("sort_order").HasDefaultValue(0).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(p => p.Code).IsUnique().HasDatabaseName("uq_subscription_plans_code");
        builder.HasIndex(p => p.IsDefaultFree).IsUnique().HasFilter("is_default_free").HasDatabaseName("uq_subscription_plans_default_free");
    }
}

public class SubscriptionPlanFeatureConfiguration : IEntityTypeConfiguration<SubscriptionPlanFeature>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanFeature> builder)
    {
        builder.ToTable("subscription_plan_features", t =>
            t.HasCheckConstraint("chk_subscription_plan_features_limit", "monthly_limit IS NULL OR monthly_limit >= 0"));

        builder.HasKey(f => new { f.PlanId, f.FeatureKey });
        builder.Property(f => f.PlanId).HasColumnName("plan_id");
        builder.Property(f => f.FeatureKey).HasColumnName("feature_key").HasMaxLength(40).IsRequired();
        builder.Property(f => f.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(f => f.MonthlyLimit).HasColumnName("monthly_limit");

        builder.HasOne<SubscriptionPlan>().WithMany(p => p.Features).HasForeignKey(f => f.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SubscriptionOrderConfiguration : IEntityTypeConfiguration<SubscriptionOrder>
{
    public void Configure(EntityTypeBuilder<SubscriptionOrder> builder)
    {
        builder.ToTable("subscription_orders", t =>
        {
            t.HasCheckConstraint("chk_subscription_orders_status", GroupColumns.CheckIn<SubscriptionOrderStatus>("status"));
            t.HasCheckConstraint("chk_subscription_orders_cycle", GroupColumns.CheckIn<SubscriptionCycle>("cycle"));
            t.HasCheckConstraint("chk_subscription_orders_amount", "amount_cents > 0");
            t.HasCheckConstraint("chk_subscription_orders_features", "jsonb_typeof(features) = 'array'");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(o => o.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(100).IsRequired();
        builder.Property(o => o.UserId).HasColumnName("user_id");
        builder.Property(o => o.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(o => o.PlanCode).HasColumnName("plan_code").HasMaxLength(40).IsRequired();
        builder.Property(o => o.PlanName).HasColumnName("plan_name").HasMaxLength(100).IsRequired();
        builder.Property(o => o.FeaturesJson).HasColumnName("features").HasColumnType("jsonb").IsRequired();
        builder.Property(o => o.Cycle).HasColumnName("cycle").HasConversion(GroupColumns.Converter<SubscriptionCycle>()).HasMaxLength(20).IsRequired();
        builder.Property(o => o.AmountCents).HasColumnName("amount_cents").IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion(GroupColumns.Converter<SubscriptionOrderStatus>()).HasMaxLength(20).IsRequired();
        builder.Property(o => o.SepayTransactionId).HasColumnName("sepay_transaction_id").HasMaxLength(100);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(o => o.NextCheckAt).HasColumnName("next_check_at").IsRequired();
        builder.Property(o => o.PaidAt).HasColumnName("paid_at");
        builder.Property(o => o.VoidedAt).HasColumnName("voided_at");
        builder.Property(o => o.ReviewReason).HasColumnName("review_reason").HasMaxLength(40);
        builder.Property(o => o.ReviewAlertedAt).HasColumnName("review_alerted_at");

        builder.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(o => o.PlanId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.InvoiceNumber).IsUnique().HasDatabaseName("uq_subscription_orders_invoice");
        builder.HasIndex(o => new { o.UserId, o.CreatedAt }).HasDatabaseName("ix_subscription_orders_user");
        builder.HasIndex(o => new { o.Status, o.CreatedAt }).HasDatabaseName("ix_subscription_orders_status");
        builder.HasIndex(o => new { o.Status, o.NextCheckAt }).HasDatabaseName("ix_subscription_orders_next_check");
    }
}

public class UserSubscriptionPeriodConfiguration : IEntityTypeConfiguration<UserSubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<UserSubscriptionPeriod> builder)
    {
        builder.ToTable("user_subscription_periods", t =>
        {
            t.HasCheckConstraint("chk_user_subscription_periods_range", "ends_at > starts_at");
            t.HasCheckConstraint("chk_user_subscription_periods_features", "jsonb_typeof(features) = 'array'");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(p => p.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(p => p.PlanCode).HasColumnName("plan_code").HasMaxLength(40).IsRequired();
        builder.Property(p => p.PlanName).HasColumnName("plan_name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.FeaturesJson).HasColumnName("features").HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.StartsAt).HasColumnName("starts_at").IsRequired();
        builder.Property(p => p.EndsAt).HasColumnName("ends_at").IsRequired();
        builder.Property(p => p.VoidedAt).HasColumnName("voided_at");
        builder.Property(p => p.Reminded7dAt).HasColumnName("reminded_7d_at");
        builder.Property(p => p.Reminded1dAt).HasColumnName("reminded_1d_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SubscriptionOrder>().WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);

        // Duy nhất theo đơn: chốt cuối cùng chống cấp quyền lợi hai lần cho một lần trả tiền.
        builder.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("uq_user_subscription_periods_order");
        builder.HasIndex(p => new { p.UserId, p.EndsAt }).HasDatabaseName("ix_user_subscription_periods_user");
        builder.HasIndex(p => p.EndsAt).HasFilter("voided_at IS NULL").HasDatabaseName("ix_user_subscription_periods_ends_at");
    }
}

public class SubscriptionUsageConfiguration : IEntityTypeConfiguration<SubscriptionUsage>
{
    public void Configure(EntityTypeBuilder<SubscriptionUsage> builder)
    {
        builder.ToTable("subscription_usage", t => t.HasCheckConstraint("chk_subscription_usage_used", "used >= 0"));

        builder.HasKey(u => new { u.UserId, u.FeatureKey, u.Month });
        builder.Property(u => u.UserId).HasColumnName("user_id");
        builder.Property(u => u.FeatureKey).HasColumnName("feature_key").HasMaxLength(40);
        builder.Property(u => u.Month).HasColumnName("month");
        builder.Property(u => u.Used).HasColumnName("used").IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
