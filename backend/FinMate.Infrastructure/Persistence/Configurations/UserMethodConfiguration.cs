using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class UserMethodConfiguration : IEntityTypeConfiguration<UserMethod>
{
    public void Configure(EntityTypeBuilder<UserMethod> builder)
    {
        builder.ToTable("user_methods", t =>
        {
            t.HasCheckConstraint("chk_user_methods_role", "role IN ('primary','supporting')");
            t.HasCheckConstraint("chk_user_methods_chosen_via", "chosen_via IN ('recommended','manual')");
            t.HasCheckConstraint("chk_user_methods_status", "status IN ('active','ended')");
            t.HasCheckConstraint("chk_user_methods_params", "jsonb_typeof(params) = 'object'");
            t.HasCheckConstraint("chk_user_methods_method_version", "method_version >= 1");

            // Đã kết thúc thì phải có mốc kết thúc, còn đang dùng thì không được có — hai cột không được lệch nhau.
            t.HasCheckConstraint("chk_user_methods_ended", "(status = 'ended') = (ended_at IS NOT NULL)");
            t.HasCheckConstraint("chk_user_methods_period", "ended_at IS NULL OR ended_at >= started_at");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.MethodId).HasColumnName("method_id").IsRequired();
        builder.Property(m => m.MethodVersion).HasColumnName("method_version").IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<UserMethodRole>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Params)
            .HasColumnName("params")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(m => m.RecommendationId).HasColumnName("recommendation_id");

        builder.Property(m => m.ChosenVia)
            .HasColumnName("chosen_via")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<MethodChoiceSource>(v, true))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<UserMethodStatus>(v, true))
            .HasMaxLength(20)
            .HasDefaultValue(UserMethodStatus.Active)
            .IsRequired();

        builder.Property(m => m.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(m => m.EndedAt).HasColumnName("ended_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Mỗi người chỉ có MỘT cách chính và MỘT cách bổ trợ đang hoạt động. Lịch sử (đã kết thúc) không bị giới hạn.
        builder.HasIndex(m => new { m.UserId, m.Role })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("uq_user_methods_active_role");

        builder.HasIndex(m => new { m.UserId, m.Status }).HasDatabaseName("idx_user_methods_user_status");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FinanceMethod>()
            .WithMany()
            .HasForeignKey(m => m.MethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MethodRecommendation>()
            .WithMany()
            .HasForeignKey(m => m.RecommendationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
