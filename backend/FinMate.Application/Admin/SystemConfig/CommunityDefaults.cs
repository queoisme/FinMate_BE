namespace FinMate.Application.Admin.SystemConfig;

/// <summary>Giá trị mặc định khi admin chưa lưu gì. 34 tỉnh, thành theo Nghị quyết 202/2025/QH15; khu vực chỉ có sẵn ở hai thành phố lớn, nơi khác admin thêm sau.</summary>
internal static class CommunityDefaults
{
    public static readonly IReadOnlyList<CommunityTopic> Topics =
    [
        new() { Code = CommunitySettings.PlacesTopic, Name = "Quán ngon, deal tốt", RequiresPlace = true, Tags = ["gia_tot", "deal", "ngon", "no_lau", "sinh_vien"] },
        new() { Code = "experience", Name = "Kinh nghiệm chi tiêu", Tags = ["tiet_kiem", "ngan_sach", "sinh_vien", "gia_dinh"] },
        new() { Code = "difficulty", Name = "Khó khăn tài chính", Tags = ["no_nan", "mat_viec", "thieu_hut", "can_loi_khuyen"] },
        new() { Code = "question", Name = "Hỏi đáp", Tags = ["ngan_sach", "tiet_kiem", "su_dung_app"] },
    ];

    public static readonly IReadOnlyList<CommunityProvince> Regions =
    [
        Province("ha-noi", "Hà Nội", ("hoan-kiem", "Hoàn Kiếm"), ("ba-dinh", "Ba Đình"), ("dong-da", "Đống Đa"), ("hai-ba-trung", "Hai Bà Trưng"), ("cau-giay", "Cầu Giấy"), ("thanh-xuan", "Thanh Xuân"), ("tay-ho", "Tây Hồ"), ("hoang-mai", "Hoàng Mai"), ("long-bien", "Long Biên"), ("nam-tu-liem", "Nam Từ Liêm"), ("bac-tu-liem", "Bắc Từ Liêm"), ("ha-dong", "Hà Đông")),
        Province("ho-chi-minh", "TP. Hồ Chí Minh", ("quan-1", "Quận 1"), ("quan-3", "Quận 3"), ("quan-4", "Quận 4"), ("quan-5", "Quận 5"), ("quan-6", "Quận 6"), ("quan-7", "Quận 7"), ("quan-8", "Quận 8"), ("quan-10", "Quận 10"), ("quan-11", "Quận 11"), ("quan-12", "Quận 12"), ("binh-thanh", "Bình Thạnh"), ("go-vap", "Gò Vấp"), ("phu-nhuan", "Phú Nhuận"), ("tan-binh", "Tân Bình"), ("tan-phu", "Tân Phú"), ("binh-tan", "Bình Tân"), ("thu-duc", "Thủ Đức"), ("nha-be", "Nhà Bè"), ("binh-chanh", "Bình Chánh"), ("hoc-mon", "Hóc Môn"), ("cu-chi", "Củ Chi"), ("can-gio", "Cần Giờ"), ("binh-duong", "Khu vực Bình Dương"), ("ba-ria-vung-tau", "Khu vực Bà Rịa - Vũng Tàu")),
        Province("hai-phong", "Hải Phòng"), Province("da-nang", "Đà Nẵng"), Province("can-tho", "Cần Thơ"), Province("hue", "Huế"),
        Province("an-giang", "An Giang"), Province("bac-ninh", "Bắc Ninh"), Province("ca-mau", "Cà Mau"), Province("cao-bang", "Cao Bằng"),
        Province("dak-lak", "Đắk Lắk"), Province("dien-bien", "Điện Biên"), Province("dong-nai", "Đồng Nai"), Province("dong-thap", "Đồng Tháp"),
        Province("gia-lai", "Gia Lai"), Province("ha-tinh", "Hà Tĩnh"), Province("hung-yen", "Hưng Yên"), Province("khanh-hoa", "Khánh Hòa"),
        Province("lai-chau", "Lai Châu"), Province("lam-dong", "Lâm Đồng"), Province("lang-son", "Lạng Sơn"), Province("lao-cai", "Lào Cai"),
        Province("nghe-an", "Nghệ An"), Province("ninh-binh", "Ninh Bình"), Province("phu-tho", "Phú Thọ"), Province("quang-ngai", "Quảng Ngãi"),
        Province("quang-ninh", "Quảng Ninh"), Province("quang-tri", "Quảng Trị"), Province("son-la", "Sơn La"), Province("tay-ninh", "Tây Ninh"),
        Province("thai-nguyen", "Thái Nguyên"), Province("thanh-hoa", "Thanh Hóa"), Province("tuyen-quang", "Tuyên Quang"), Province("vinh-long", "Vĩnh Long"),
    ];

    // Cụm hai từ trở lên: một từ đơn như "ăn", "phở" sau khi bỏ dấu trùng với tên người ("An") hay chữ khác ("phố"), và giao dịch chuyển khoản có thể mang tên người thật.
    public static readonly IReadOnlyList<string> FoodKeywords =
    [
        "quán ăn", "quán cơm", "quán phở", "quán bún", "quán cháo", "quán nướng", "cơm tấm", "cơm gà", "cơm văn phòng", "cơm bình dân", "phở bò", "phở gà", "bún bò", "bún chả", "bún đậu", "bánh mì", "mì quảng", "hủ tiếu", "lẩu", "cà phê", "cafe", "coffee", "trà sữa", "nhà hàng", "ăn sáng", "ăn trưa", "ăn tối", "highlands", "phúc long", "kfc", "lotteria", "jollibee", "grabfood", "shopeefood",
    ];

    public static readonly IReadOnlyList<string> FoodExcludes = [];

    public static readonly IReadOnlyList<string> BannedWords =
    [
        "lồn", "cặc", "địt", "vay nhanh", "vay online", "vay không thế chấp", "giải ngân trong ngày", "bốc họ", "cam kết lợi nhuận", "lợi nhuận mỗi ngày", "kèo đầu tư", "nhóm kín đầu tư", "liên hệ zalo", "kết bạn zalo", "inbox mình", "ib mình",
    ];

    private static CommunityProvince Province(string code, string name, params (string Code, string Name)[] areas)
        => new() { Code = code, Name = name, Areas = areas.Select(a => new CommunityArea { Code = a.Code, Name = a.Name }).ToList() };
}
