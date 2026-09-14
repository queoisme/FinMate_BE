using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinMate.Infrastructure.Persistence.Configurations;

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens", t =>
        {
            t.HasCheckConstraint("chk_device_tokens_platform", "platform IN ('android','ios','web')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.UserId).HasColumnName("user_id").IsRequired();

        // FCM hiện cấp token ~163 ký tự nhưng Google không cam kết độ dài — đây là định dạng
        // đục (opaque) của bên thứ ba. Không đặt HasMaxLength: token dài hơn giới hạn tự đặt
        // sẽ bị cắt cụt khi ghi, và một token cụt thì im lặng không gửi được cho ai.
        builder.Property(d => d.Token).HasColumnName("token").IsRequired();

        builder.Property(d => d.Platform)
            .HasColumnName("platform")
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<DevicePlatform>(v, true))
            .HasColumnType("text")
            .IsRequired();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.LastSeenAt).HasColumnName("last_seen_at").IsRequired();

        // DUY NHẤT trên token, không phải trên (user_id, token). Một máy chỉ thuộc về một
        // người: nếu B đăng nhập trên máy của A mà dòng cũ còn đó, thông báo tài chính của B
        // vẫn tiếp tục đẩy xuống đúng cái máy đó cho A đọc.
        builder.HasIndex(d => d.Token)
            .IsUnique()
            .HasDatabaseName("uq_device_tokens_token");

        builder.HasIndex(d => d.UserId)
            .HasDatabaseName("idx_device_tokens_user_id");

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
