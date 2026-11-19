using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using SkiaSharp;

namespace FinMate.Infrastructure.Persistence.Seed;

public static partial class DashboardDevelopmentSeeder
{
    private static void AddCommunity(FinMateDbContext db, Dictionary<string, User> people, DateTimeOffset now)
    {
        var place = new CommunityPlace { Id = Id("place:lunch"), Name = $"{Label} Quán cơm văn phòng mô phỏng",
            NameKey = "kiem thu quan com van phong mo phong", ProvinceCode = "ho-chi-minh", AreaCode = "quan-1",
            CreatedAt = now.AddDays(-7), UpdatedAt = now };
        db.CommunityPlaces.Add(place);
        (string Key, string User, string Topic, string Title, string Body)[] posts = [
            ("lunch", "an", "places", "Lên kế hoạch bữa trưa trong tuần", "Tình huống mô phỏng: chuẩn bị ngân sách bữa trưa và so sánh với tự nấu. Địa điểm, giá và ảnh minh họa không phải đánh giá một quán có thật."),
            ("budget", "binh", "experience", "Dành ngân sách theo tuần khi thu nhập thay đổi", "Tình huống mô phỏng: tách tiền thuê nhà, dự phòng và phần chi tiêu linh hoạt ngay khi nhận thu nhập, rồi kiểm tra lại mỗi tuần."),
            ("report", "chi", "question", "Cần rà soát một lời mời vay tiền", "Tình huống kiểm duyệt mô phỏng. Bài này dùng để kiểm tra báo cáo nội dung; không chứa lời mời vay hay thông tin liên hệ thật."),
            ("discussion", "an", "question", "Khi nào nên điều chỉnh hạn mức ăn uống?", "Tình huống mô phỏng: xem lịch sử theo tháng trước khi thay đổi ngân sách, tránh nâng hạn mức chỉ để làm biến mất cảnh báo.") ];
        for (var i = 0; i < posts.Length; i++)
        {
            var row = posts[i];
            db.CommunityPosts.Add(new CommunityPost { Id = Id($"post:{row.Key}"), AuthorId = people[row.User].Id,
                Topic = row.Topic, Title = $"{Label} {row.Title}", Body = $"{Label} {row.Body}",
                PlaceId = row.Key == "lunch" ? place.Id : null, PriceCents = row.Key == "lunch" ? 35_000 : null,
                Tags = row.Key == "lunch" ? ["sinh_vien"] : ["ngan_sach"], IsAnonymous = true,
                Status = CommunityContentStatus.Visible, CreatedAt = now.AddDays(-4).AddHours(i), UpdatedAt = now.AddDays(-4).AddHours(i) });
        }
        db.CommunityComments.Add(new CommunityComment { Id = Id("comment:budget"), PostId = Id("post:budget"), AuthorId = people["chi"].Id,
            Body = $"{Label} Mình thử chia hạn mức theo tuần để dễ theo dõi phần còn lại.", IsAnonymous = true,
            CreatedAt = now.AddDays(-2), UpdatedAt = now.AddDays(-2) });
        AddCommunityReports(db, people, now);
        AddCommunityImages(db, now);
    }

    private static void AddCommunityReports(FinMateDbContext db, Dictionary<string, User> people, DateTimeOffset now)
    {
        db.CommunityReports.Add(new CommunityReport { Id = Id("report:post"), PostId = Id("post:report"), ReporterId = people["an"].Id,
            Reason = CommunityReportReason.Spam, Priority = CommunityReportPriority.Normal, Status = CommunityReportStatus.Open, CreatedAt = now.AddHours(-3) });
        db.CommunityReports.Add(new CommunityReport { Id = Id("report:comment"), CommentId = Id("comment:budget"), ReporterId = people["binh"].Id,
            Reason = CommunityReportReason.Other, Priority = CommunityReportPriority.Normal, Status = CommunityReportStatus.Open, CreatedAt = now.AddHours(-2) });
    }

    private static void AddCommunityImages(FinMateDbContext db, DateTimeOffset now)
    {
        using var bitmap = new SKBitmap(800, 500);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#dce8f5"));
        using var paint = new SKPaint { Color = SKColor.Parse("#24384f"), IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 32);
        canvas.DrawText("FINMATE - DEVELOPMENT TEST IMAGE", 32, 80, SKTextAlign.Left, font, paint);
        canvas.DrawText("SYNTHETIC MENU / NOT A REAL RECEIPT", 32, 145, SKTextAlign.Left, font, paint);
        canvas.DrawText("Lunch planning: rice, vegetables, water", 32, 250, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        var data = encoded.ToArray();
        db.CommunityPostImages.Add(new CommunityPostImage { Id = Id("image:pending"), PostId = Id("post:lunch"), Data = data,
            Width = bitmap.Width, Height = bitmap.Height, SizeBytes = data.Length, Position = 0,
            Status = CommunityImageStatus.Pending, CreatedAt = now.AddHours(-4) });
    }
}
