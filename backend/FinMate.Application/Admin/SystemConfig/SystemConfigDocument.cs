namespace FinMate.Application.Admin.SystemConfig;

/// <summary>Tên các tài liệu cấu hình hệ thống (cột <c>name</c> của <c>system_config_versions</c>).</summary>
public static class SystemConfigNames
{
    public const string App = "app";
}

public sealed record MaintenanceSettings
{
    public bool Enabled { get; init; }
    public string Title { get; init; } = "FinMate đang bảo trì";
    public string Message { get; init; } = "Chúng tôi đang nâng cấp hệ thống để phục vụ bạn tốt hơn. Vui lòng quay lại sau ít phút.";
    public DateTimeOffset? EstimatedEndAt { get; init; }
}

public sealed record AppVersionSettings
{
    /// <summary>Phiên bản app thấp hơn mức này phải cập nhật mới dùng được.</summary>
    public string MinVersion { get; init; } = "1.0.0";
    public string LatestVersion { get; init; } = "1.0.0";

    /// <summary>Bật thì app cũ hơn <see cref="LatestVersion"/> bị buộc cập nhật (không chỉ dưới <see cref="MinVersion"/>).</summary>
    public bool ForceUpdate { get; init; }
    public string StoreUrl { get; init; } = string.Empty;
}

public sealed record FeatureSettings
{
    public bool ChatAi { get; init; } = true;
    public bool Voice { get; init; } = true;
    public bool BillScan { get; init; } = true;
    public bool NotificationReading { get; init; } = true;

    /// <summary>Tài chính nhóm. Mặc định tắt: chỉ bật sau khi app đã có màn hình và người dùng đã đồng ý Điều khoản mới.</summary>
    public bool Groups { get; init; }

    /// <summary>Ticket hỗ trợ cho khách. Mặc định tắt: chỉ bật sau khi app có màn hình và web vận hành đã có xác thực 2 lớp.</summary>
    public bool Support { get; init; }

    /// <summary>Cảnh báo giá tăng. Mặc định tắt: chỉ bật sau khi app xử lý được loại cảnh báo này. Việc đọc dữ liệu vẫn chạy khi tắt.</summary>
    public bool MarketAlerts { get; init; }

    /// <summary>Cảnh báo khi giá thị trường làm ngày đạt mục tiêu tiết kiệm lùi xa (<c>goal_market_shift</c>). Mặc định tắt cho tới khi app xử lý được loại này. Gắn mục tiêu với giá vẫn dùng được khi tắt.</summary>
    public bool GoalMarketAlerts { get; init; }

    /// <summary>Cộng đồng. Mặc định tắt: chỉ bật khi app đã có màn hình, câu chữ đồng ý đã được duyệt và đã có người trực duyệt ảnh.</summary>
    public bool Community { get; init; }

    /// <summary>Gói đăng ký. Mặc định tắt: tắt thì không chặn, không đếm lượt, không tạo đơn mới. Tiền đã trả vẫn được ghi nhận khi tắt.</summary>
    public bool Subscriptions { get; init; }

    /// <summary>Quỹ chung của nhóm (Phase 85). Mặc định tắt; chỉ có tác dụng khi <see cref="Groups"/> cũng bật. Tắt thì API quỹ trả 422, nhưng
    /// xoá giao dịch cá nhân gắn quỹ vẫn đồng bộ dòng quỹ và chi từ quỹ vẫn tính vào ngân sách nhóm, để số của nhóm không lệch.</summary>
    public bool GroupFunds { get; init; }

    /// <summary>Xuất dữ liệu (Phase 91). Mặc định BẬT vì phục vụ quyền theo luật; chỉ tắt khi sự cố. Tắt thì không nhận CSV, yêu cầu gói hay link mới,
    /// job dựng gói tạm dừng (yêu cầu nằm chờ), dọn dẹp vẫn chạy.</summary>
    public bool DataExport { get; init; } = true;
}

public sealed record GroupSettings
{
    public int MaxMembers { get; init; } = 20;
    public int MaxGroupsPerUser { get; init; } = 5;
    public int MaxPendingInvitations { get; init; } = 20;
    public int InvitationTtlDays { get; init; } = 7;

    /// <summary>Số quỹ CHƯA lưu trữ tối đa của một nhóm (BR-FUND-01, D16).</summary>
    public int MaxFundsPerGroup { get; init; } = 3;

    /// <summary>Một người chỉ bị nhắc nợ một lần trong khoảng này (giờ), tính cho mỗi nhóm.</summary>
    public int DebtReminderCooldownHours { get; init; } = 24;

    /// <summary>Số hóa đơn tối đa mỗi trang.</summary>
    public int MaxBillsPerPage { get; init; } = 50;

    /// <summary>Đầu của link mời công khai; link là <c>{InviteLinkBaseUrl}/{token}</c>. Phải là https.</summary>
    public string InviteLinkBaseUrl { get; init; } = "https://finmate.app/join";

    /// <summary>Mỗi người tìm/mời theo FinMate ID tối đa chừng này lần trong một cửa sổ (chống dò người dùng).</summary>
    public int UserSearchLimit { get; init; } = 20;

    public int UserSearchWindowMinutes { get; init; } = 60;
    public int MaxInviteSuggestions { get; init; } = 10;
}

/// <summary>Hạn SLA tính bằng phút kể từ lúc tạo ticket, và các giới hạn của ticket.</summary>
public sealed record SupportSettings
{
    public int P1FirstResponseMinutes { get; init; } = 15;
    public int P1ResolveMinutes { get; init; } = 240;
    public int P2FirstResponseMinutes { get; init; } = 60;
    public int P2ResolveMinutes { get; init; } = 1440;
    public int P3FirstResponseMinutes { get; init; } = 240;
    public int P3ResolveMinutes { get; init; } = 4320;
    public int MaxOpenTicketsPerUser { get; init; } = 10;
    public int MaxAttachmentsPerMessage { get; init; } = 3;

    /// <summary>Ca đã giải quyết mà khách không phản hồi sau chừng này ngày thì tự đóng.</summary>
    public int AutoCloseResolvedDays { get; init; } = 7;

    /// <summary>Ảnh trong ticket bị xóa sau khi ca đóng chừng này ngày.</summary>
    public int AttachmentRetentionDays { get; init; } = 30;
}

/// <summary>Thông báo trong web vận hành (chuông của Staff, OpsManager, Admin).</summary>
public sealed record OpsNotificationSettings
{
    /// <summary>Tắt thì không tạo thông báo mới nào; thông báo đã có vẫn đọc được.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Tên loại (dạng snake_case, ví dụ <c>community_report</c>) bị tắt. Loại không có trong danh sách là đang bật.</summary>
    public IReadOnlyList<string> DisabledTypes { get; init; } = [];

    /// <summary>Thông báo cũ hơn chừng này ngày bị xóa, đã đọc hay chưa.</summary>
    public int RetentionDays { get; init; } = 30;
}

/// <summary>Ngưỡng giám sát hệ thống. Mức độ của từng dịch vụ nằm trong code, xem <c>MonitoredServices</c>.</summary>
public sealed record MonitoringSettings
{
    /// <summary>Số lần kiểm tra lỗi liên tiếp mới tính là sự cố, để một lần chập chờn không thành cảnh báo.</summary>
    public int ProbeFailureThreshold { get; init; } = 3;
    public int ProbeRecoveryThreshold { get; init; } = 3;
    public int ErrorCountThreshold { get; init; } = 10;
    public int ErrorWindowMinutes { get; init; } = 5;

    /// <summary>Lỗi không lặp lại trong chừng này phút thì sự cố tự đóng.</summary>
    public int ErrorQuietMinutes { get; init; } = 30;
    public int ProviderFailureThreshold { get; init; } = 3;
    public int ReminderMinutes { get; init; } = 15;
    public int ErrorRetentionDays { get; init; } = 30;

    /// <summary>
    /// Giải thích lỗi bằng lời thường cho trang Giám sát hệ thống. Khớp theo tên kiểu lỗi (chứa chuỗi <c>Match</c>, không phân biệt hoa thường), mục đầu tiên khớp được dùng.
    /// Admin sửa/thêm bằng cấu hình, không phải sửa code. Chỉ là chú giải: không đổi cách ghi nhận hay gom sự cố.
    /// </summary>
    public IReadOnlyList<ErrorHint> ErrorHints { get; init; } =
    [
        new() { Match = "AIServiceUnavailable", Title = "Dịch vụ AI không phản hồi", Explanation = "Hệ thống không kết nối được tới dịch vụ phân tích AI (đang tắt, đang khởi động lại hoặc quá tải). Các việc cần AI sẽ không chạy hoặc dùng luật dự phòng.", Action = "Mở tab Dịch vụ xem \"Phân tích AI\" có đang chạy không; khởi động lại dịch vụ AI nếu cần." },
        new() { Match = "OperationCanceled", Title = "Yêu cầu bị hủy giữa chừng", Explanation = "Người dùng đóng ứng dụng hoặc mất mạng trước khi hệ thống trả lời xong, hoặc yêu cầu chạy quá lâu nên bị ngắt. Xuất hiện lẻ tẻ thì không phải lỗi hệ thống.", Action = "Chỉ cần xử lý nếu số lượng tăng đột biến." },
        new() { Match = "TaskCanceled", Title = "Yêu cầu bị hủy giữa chừng", Explanation = "Một yêu cầu bị ngắt trước khi hoàn tất (mất mạng hoặc quá thời gian chờ).", Action = "Chỉ cần xử lý nếu số lượng tăng đột biến." },
        new() { Match = "SwaggerGeneratorException", Title = "Không dựng được trang tài liệu API", Explanation = "Trang tài liệu API (Swagger) không tạo được vì có một API khai báo chưa đúng. Không ảnh hưởng người dùng ứng dụng.", Action = "Báo đội phát triển, kèm thời điểm lỗi." },
        new() { Match = "TimeoutException", Title = "Quá thời gian chờ", Explanation = "Một thao tác chờ quá lâu mà không có phản hồi (cơ sở dữ liệu hoặc dịch vụ bên ngoài chậm).", Action = "Xem tab Dịch vụ để biết dịch vụ nào đang chậm." },
        new() { Match = "HttpRequestException", Title = "Không gọi được dịch vụ bên ngoài", Explanation = "Hệ thống không kết nối được tới một dịch vụ bên ngoài (đọc chữ hóa đơn, nhận diện giọng nói, ngân hàng...).", Action = "Xem tab Dịch vụ và tab Chất lượng thông báo." },
        new() { Match = "Npgsql", Title = "Lỗi cơ sở dữ liệu", Explanation = "Hệ thống không đọc hoặc ghi được cơ sở dữ liệu.", Action = "Kiểm tra cơ sở dữ liệu có đang chạy; báo đội phát triển nếu lặp lại." },
        new() { Match = "Redis", Title = "Lỗi bộ nhớ đệm", Explanation = "Hệ thống không kết nối được bộ nhớ đệm Redis. Ứng dụng có thể chậm hơn nhưng dữ liệu không mất.", Action = "Kiểm tra Redis có đang chạy." },
        new() { Match = "DbUpdateConcurrency", Title = "Hai thao tác ghi cùng lúc", Explanation = "Hai yêu cầu cùng sửa một dữ liệu nên một bên bị từ chối. Người dùng thử lại là được.", Action = "Chỉ cần xử lý nếu xuất hiện liên tục." },
        new() { Match = "JsonException", Title = "Dữ liệu gửi lên sai định dạng", Explanation = "Một yêu cầu gửi lên dữ liệu không đọc được.", Action = "Báo đội phát triển nếu lặp lại nhiều." },
        new() { Match = "NullReference", Title = "Lỗi lập trình (thiếu dữ liệu)", Explanation = "Chương trình gặp dữ liệu trống ở chỗ không dự kiến. Đây là lỗi của phần mềm, không phải do người dùng.", Action = "Báo đội phát triển, kèm mã đối chiếu trong phần chi tiết." },
    ];
}

/// <summary>Chú giải cho một loại lỗi trên trang Giám sát hệ thống.</summary>
public sealed record ErrorHint
{
    /// <summary>Chuỗi có trong tên kiểu lỗi (ví dụ <c>TaskCanceled</c>).</summary>
    public string Match { get; init; } = string.Empty;

    /// <summary>Tên lỗi bằng lời thường, ngắn.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Lỗi này nghĩa là gì và ảnh hưởng ra sao.</summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>Người vận hành nên làm gì (có thể để trống).</summary>
    public string Action { get; init; } = string.Empty;
}

/// <summary>Một loại chi phí hệ thống theo dõi giá. Thêm loại mới bằng cấu hình, không phải sửa code. Từ khóa so khớp theo cả từ và không phân biệt dấu, vì thông báo ngân hàng thường viết không dấu.</summary>
public sealed record MarketCostType
{
    public string Key { get; init; } = string.Empty;

    /// <summary>Tên trong câu cảnh báo: "Giá {Label} đang tăng". Do admin đặt, không bao giờ lấy từ trang web hay AI.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Mô tả gửi cho AI để nhận ra chủ đề này trong bài báo.</summary>
    public string AiDescription { get; init; } = string.Empty;

    /// <summary>Lọc bài trước khi gửi AI. Giữ phân biệt dấu, vì bỏ dấu thì "tham gia điện ảnh" thành "giá điện".</summary>
    public IReadOnlyList<string> NewsKeywords { get; init; } = [];

    /// <summary>
    /// Tìm trong tên cửa hàng, mô tả và tên danh mục của giao dịch, ở mọi danh mục. Khớp cả từ, không phân biệt dấu.
    /// Kết thúc bằng <c>*</c> thì khớp phần đầu của từ, dùng cho mã như <c>EVNHCMC</c>, <c>PETROLIMEX04</c>.
    /// </summary>
    public IReadOnlyList<string> TransactionKeywords { get; init; } = [];

    /// <summary>Cụm bị bỏ trước khi so khớp, để "tiền điện thoại" không thành tiền điện.</summary>
    public IReadOnlyList<string> TransactionExcludes { get; init; } = [];

    /// <summary>Mọi giao dịch trong các danh mục hệ thống này đều tính, dùng khi khoản chi quá đa dạng để liệt kê từ khóa.</summary>
    public IReadOnlyList<string> CategorySlugs { get; init; } = [];

    /// <summary>Có giá thật ở <c>fuel_prices</c>: cảnh báo theo giá và ước tính được số tiền, không qua sự kiện từ tin bài.</summary>
    public bool HasPriceTable { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed record MarketDataSettings
{
    /// <summary>Số nguồn phải cho cùng một giá thì giá mới được xác nhận. Hạ xuống 1 khi một nguồn ngừng hoạt động.</summary>
    public int MinAgreeingSources { get; init; } = 2;
    public int MaxDocumentsPerRun { get; init; } = 20;
    public long MinPriceCents { get; init; } = 10_000;
    public long MaxPriceCents { get; init; } = 60_000;

    /// <summary>Đổi hơn mức này so với giá đang lưu thì không nhận: nhiều khả năng đọc sai hơn là giá thật.</summary>
    public int MaxChangePercent { get; init; } = 20;

    /// <summary>Số tên miền khác nhau cùng đưa tin thì mới thành sự kiện. Báo hay đăng lại của nhau nên đây chỉ là một lớp lọc, không chứng minh là đúng.</summary>
    public int MinEventDomains { get; init; } = 2;

    /// <summary>Tắt thì sự kiện từ tin bài phải chờ admin duyệt. AI đọc sai mà tự gửi thì nhiều người nhận tin sai cùng lúc.</summary>
    public bool AutoPublishNewsEvents { get; init; }

    /// <summary>Trần số bài gửi AI mỗi ngày, tính cả lần lỗi, để giới hạn chi phí.</summary>
    public int MaxAiDocumentsPerDay { get; init; } = 100;

    /// <summary>Loại xăng dùng để so sánh khi cảnh báo.</summary>
    public string ReferenceFuelKey { get; init; } = "e10_ron95_3";

    /// <summary>Tăng từ mức này so với mốc cách đây <see cref="CompareWindowDays"/> ngày thì mới xét cảnh báo. Giá đổi vài ngày một lần nên không so với kỳ liền trước.</summary>
    public int RiseThresholdPercent { get; init; } = 3;
    public int CompareWindowDays { get; init; } = 14;
    public int AlertCooldownDays { get; init; } = 14;
    public int SpendLookbackDays { get; init; } = 60;
    public int MinCategoryTransactions { get; init; } = 2;

    /// <summary>Lần chi gần nhất phải trong chừng này ngày: người đã bán xe không cần nghe về giá xăng nữa.</summary>
    public int LastSpendMaxDays { get; init; } = 45;

    /// <summary>Khoản này phải chiếm ít nhất chừng này phần trăm tổng chi trong kỳ xét.</summary>
    public int MinSpendSharePercent { get; init; } = 2;

    /// <summary>Ước tính tốn thêm dưới mức này thì không làm phiền người dùng.</summary>
    public long MinExtraCostCents { get; init; } = 5_000;

    public IReadOnlyList<MarketCostType> CostTypes { get; init; } =
    [
        new()
        {
            Key = "fuel",
            Label = "xăng",
            AiDescription = "xăng dầu bán lẻ",
            TransactionKeywords = ["xăng", "petrolimex*", "pvoil*", "mipec*", "chxd*"],
            NewsKeywords = ["giá xăng", "xăng dầu", "ron 95", "ron95", "ron 92", "ron92", "e5 ron*", "e10"],
            HasPriceTable = true,
        },
        new()
        {
            Key = "electricity",
            Label = "điện",
            AiDescription = "giá điện sinh hoạt",
            TransactionKeywords = ["evn*", "tiền điện", "điện lực", "hóa đơn điện"],
            TransactionExcludes = ["tiền điện thoại", "hóa đơn điện thoại", "hóa đơn điện tử"],
            NewsKeywords = ["giá điện", "tiền điện", "biểu giá điện"],
        },
        new()
        {
            Key = "water",
            Label = "nước",
            AiDescription = "giá nước sinh hoạt",
            TransactionKeywords = ["tiền nước", "cấp nước", "sawaco*", "hóa đơn nước"],
            TransactionExcludes = ["tiền nước mía", "tiền nước ngọt", "tiền nước uống"],
            NewsKeywords = ["giá nước", "tiền nước"],
        },
        new()
        {
            Key = "food_price",
            Label = "thực phẩm",
            AiDescription = "giá thực phẩm: thịt, rau, gạo, trứng",
            TransactionKeywords = ["bách hóa xanh", "winmart", "co.opmart", "coopmart", "đi chợ", "siêu thị"],
            NewsKeywords = ["giá thực phẩm", "giá thịt", "giá heo", "giá lợn", "giá rau", "giá gạo", "giá trứng"],
            CategorySlugs = ["food"],
        },
        new()
        {
            Key = "cooking_gas",
            Label = "gas nấu ăn",
            AiDescription = "giá gas nấu ăn, khí hóa lỏng bán lẻ",
            TransactionKeywords = ["gas", "bình gas", "đổi gas", "tiền gas"],
            TransactionExcludes = ["nước có gas", "có gas"],
            NewsKeywords = ["giá gas", "giá khí hóa lỏng", "gas bán lẻ"],
        },
    ];

    /// <summary>
    /// Tên miền được dùng làm nguồn. Không có tên trong danh sách thì không thêm được nguồn, nguồn đã có bị bỏ qua và tin không được đếm.
    /// Danh sách mặc định chỉ gồm trang đã kiểm ngày 2026-11-23; số giấy phép chép từ chân trang của chính trang đó.
    /// </summary>
    public IReadOnlyList<MarketTrustedDomain> TrustedDomains { get; init; } =
    [
        new() { Domain = "nso.gov.vn", Tier = MarketTrustedDomain.Official, Owner = "Cục Thống kê" },
        new() { Domain = "moit.gov.vn", Tier = MarketTrustedDomain.Official, Owner = "Bộ Công Thương" },
        new() { Domain = "sbv.gov.vn", Tier = MarketTrustedDomain.Official, Owner = "Ngân hàng Nhà nước Việt Nam" },
        new() { Domain = "petrolimex.com.vn", Tier = MarketTrustedDomain.Official, Owner = "Tập đoàn Xăng dầu Việt Nam", LicenseRef = "1900/GP-TTĐT, Sở TT&TT Hà Nội, 08/07/2020", CheckedAt = "2026-11-23" },
        new() { Domain = "sjc.com.vn", Tier = MarketTrustedDomain.Official, Owner = "Công ty Vàng bạc Đá quý Sài Gòn - SJC" },
        new() { Domain = "pnj.com.vn", Tier = MarketTrustedDomain.Official, Owner = "Công ty Vàng bạc Đá quý Phú Nhuận - PNJ" },
        new() { Domain = "doji.vn", Tier = MarketTrustedDomain.Official, Owner = "Tập đoàn Vàng bạc Đá quý DOJI" },
        new() { Domain = "btmc.vn", Tier = MarketTrustedDomain.Official, Owner = "Công ty Bảo Tín Minh Châu" },
        new() { Domain = "24h.com.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Công ty CP Quảng cáo trực tuyến 24H", LicenseRef = "180/GP-STTTT, Sở TT&TT Hà Nội, 11/12/2024", CheckedAt = "2026-11-23" },
        new() { Domain = "cafef.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Công ty Cổ phần VCCorp", LicenseRef = "2216/GP-TTĐT, Sở TT&TT Hà Nội, 10/04/2019", CheckedAt = "2026-11-23" },
        new() { Domain = "tuoitre.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Cơ quan Báo và Phát thanh, Truyền hình TP.HCM", LicenseRef = "561/GP-BTTTT, 25/11/2022", CheckedAt = "2026-11-23" },
        new() { Domain = "nld.com.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Cơ quan Báo và Phát thanh, Truyền hình TP.HCM", LicenseRef = "561/GP-BTTTT, 25/11/2022", CheckedAt = "2026-11-23" },
        new() { Domain = "thanhnien.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Báo Thanh Niên", LicenseRef = "110/GP-BTTTT, 24/03/2020", CheckedAt = "2026-11-23" },
        new() { Domain = "dantri.com.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Báo Dân trí (Bộ Nội vụ)", LicenseRef = "15/GP-BVHTTDL, 14/04/2025", CheckedAt = "2026-11-23" },
        new() { Domain = "vietnamnet.vn", Tier = MarketTrustedDomain.Licensed, Owner = "Báo VietNamNet (Bộ Dân tộc và Tôn giáo)", LicenseRef = "146/GP-BVHTTDL, 17/10/2025", CheckedAt = "2026-11-23" },
    ];

    /// <summary>Bảng giá vàng, đọc trên cùng nguồn <c>price_table</c> với giá xăng.</summary>
    public GoldSettings Gold { get; init; } = new();

    /// <summary>Giá thị trường mà người dùng gắn được với mục tiêu tiết kiệm. Hiện chỉ có giá vàng (giá bán ra).</summary>
    public IReadOnlyList<GoalMarketTopic> GoalTopics { get; init; } =
    [
        new() { Key = "gold", Label = "vàng", GoldKey = "sjc_bar", NameKeywords = ["vàng"] },
    ];

    public GoalMarketAlertSettings GoalAlerts { get; init; } = new();

    /// <summary>Từ giá đã xác nhận, soạn bản NHÁP giả định tốc độ tăng giá cho agent. Không bao giờ tự duyệt.</summary>
    public IReadOnlyList<MarketAssumptionRule> AssumptionRules { get; init; } =
    [
        new() { GoldKey = "sjc_bar", AssumptionKey = "gold_price_growth" },
    ];

    /// <summary>Chữ hiển thị của link tới thông cáo giá xăng mới nhất trên trang chỉ mục (nguồn loại <c>fuel_release</c>). Link đầu tiên chứa cụm này được đọc.</summary>
    public string FuelReleaseLinkText { get; init; } = "Thông tin về việc điều hành giá xăng dầu";

    /// <summary>Tên hàng trong thông cáo điều hành giá xăng dầu của Bộ Công Thương → mã giá. Câu có dạng "{tên}: không cao hơn {số} đồng/lít".</summary>
    public IReadOnlyList<FuelReleaseName> FuelReleaseNames { get; init; } =
    [
        new() { Name = "Xăng E5RON92", FuelKey = "e5_ron92_2" },
        new() { Name = "Xăng E10RON95-III", FuelKey = "e10_ron95_3" },
        new() { Name = "Dầu điêzen 0.05S", FuelKey = "do_005s_2" },
    ];
}

/// <summary>
/// Một tên miền được tin dùng. <see cref="Official"/>: nơi tự công bố con số (cơ quan nhà nước, doanh nghiệp phát hành giá), một nguồn là đủ.
/// <see cref="Licensed"/>: báo hoặc trang tổng hợp có giấy phép, phải có đủ đơn vị chủ quản khác nhau cùng cho một con số.
/// </summary>
public sealed record MarketTrustedDomain
{
    public const string Official = "official";
    public const string Licensed = "licensed";

    /// <summary>Tên miền gốc, chữ thường, ví dụ <c>24h.com.vn</c>. Tên miền con tính chung.</summary>
    public string Domain { get; init; } = string.Empty;
    public string Tier { get; init; } = Licensed;

    /// <summary>Đơn vị chủ quản. Hai tên miền cùng chủ quản chỉ tính là một nguồn khi so khớp.</summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary>Số giấy phép và nơi cấp. Bắt buộc với <see cref="Licensed"/>.</summary>
    public string LicenseRef { get; init; } = string.Empty;

    /// <summary>Ngày người thêm đã kiểm giấy phép, <c>yyyy-MM-dd</c>.</summary>
    public string CheckedAt { get; init; } = string.Empty;
}

/// <summary>
/// Đọc giá vàng trên trang bảng giá. Mỗi dòng: tên (ví dụ "Vàng miếng SJC") rồi các số; giữa các số chỉ được có <see cref="RowWords"/>, số tăng giảm và phần trăm.
/// Đơn vị lấy từ nhãn "Đơn vị: …" gần nhất phía sau dòng, quy ra đồng/lượng theo <see cref="Units"/>. Số ngoài khoảng giá bị bỏ (đó là cột tăng giảm);
/// hai số đầu còn lại là mua vào, bán ra.
/// </summary>
public sealed record GoldSettings
{
    public bool Enabled { get; init; } = true;

    public IReadOnlyList<GoldRow> Rows { get; init; } =
    [
        new() { Key = "sjc_bar", Label = "vàng miếng SJC", Names = ["Vàng miếng SJC", "SJC"] },
    ];

    /// <summary>Chữ được phép đứng giữa tên dòng và các số, so không phân biệt hoa thường.</summary>
    public IReadOnlyList<string> RowWords { get; init; } = ["mua", "vào", "bán", "ra"];

    /// <summary>Cách viết đơn vị sau "Đơn vị:" → số đồng mỗi lượng của một đơn vị số trên trang. Dấu cách quanh "/" được bỏ trước khi so.</summary>
    public IReadOnlyList<GoldUnit> Units { get; init; } =
    [
        new() { Text = "nghìn đồng/lượng", Multiplier = 1_000 },
        new() { Text = "ngàn đồng/lượng", Multiplier = 1_000 },
        new() { Text = "x1000đ/lượng", Multiplier = 1_000 },
        new() { Text = "đồng/lượng", Multiplier = 1 },
        new() { Text = "đồng/chỉ", Multiplier = 10 },
    ];

    /// <summary>Khoảng hợp lý của một lượng. Bắt luôn lỗi nhầm đơn vị.</summary>
    public long MinPriceCents { get; init; } = 50_000_000;
    public long MaxPriceCents { get; init; } = 500_000_000;

    /// <summary>Đổi hơn mức này so với giá đang lưu thì không nhận.</summary>
    public int MaxChangePercent { get; init; } = 10;

    /// <summary>Chênh mua vào và bán ra tối đa, tính trên giá bán.</summary>
    public int MaxSpreadPercent { get; init; } = 10;
}

/// <summary>Một giá thị trường mà mục tiêu tiết kiệm theo được.</summary>
public sealed record GoalMarketTopic
{
    /// <summary>Mã lưu ở <c>saving_goals.market_topic</c>. Không đổi sau khi đã có mục tiêu gắn với nó.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Tên trong câu cảnh báo: "Giá {Label} …". Do admin đặt.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Mã giá vàng (<c>marketData.gold.rows</c>) làm giá của chủ đề; dùng giá bán ra.</summary>
    public string GoldKey { get; init; } = string.Empty;

    /// <summary>Tên mục tiêu chứa một trong các từ này thì app gợi ý gắn chủ đề. Chỉ gợi ý, không tự gắn.</summary>
    public IReadOnlyList<string> NameKeywords { get; init; } = [];
    public bool Enabled { get; init; } = true;
}

/// <summary>Khi nào báo rằng giá làm mục tiêu lùi xa. Phải đủ mức tăng giá VÀ (lùi đủ ngày HOẶC mục tiêu có hạn chuyển từ kịp sang trễ).</summary>
public sealed record GoalMarketAlertSettings
{
    /// <summary>Giá tăng ít nhất chừng này phần trăm so với giá lúc gắn.</summary>
    public int MinChangePercent { get; init; } = 5;

    /// <summary>Ngày đạt dự kiến lùi ít nhất chừng này ngày.</summary>
    public int MinShiftDays { get; init; } = 30;

    /// <summary>Không báo lại cùng một mục tiêu trong chừng này ngày.</summary>
    public int CooldownDays { get; init; } = 30;
}

/// <summary>Một giả định soạn từ giá vàng: % thay đổi giá bán ra trong <see cref="WindowDays"/> ngày, quy ra mỗi năm.</summary>
public sealed record MarketAssumptionRule
{
    public string GoldKey { get; init; } = string.Empty;

    /// <summary>Khóa giả định (<c>economic_assumptions.key</c>) mà agent tra, ví dụ <c>gold_price_growth</c>.</summary>
    public string AssumptionKey { get; init; } = string.Empty;

    /// <summary>Chưa có giá từ chừng này ngày trước thì không soạn: vài tuần dữ liệu không nói được tốc độ một năm.</summary>
    public int WindowDays { get; init; } = 365;

    /// <summary>Chỉ soạn nháp mới khi lệch so với bản đang dùng từ chừng này điểm cơ bản (100 = 1 điểm %).</summary>
    public int MinDeltaBps { get; init; } = 200;
    public bool Enabled { get; init; } = true;
}

public sealed record GoldRow
{
    /// <summary>Mã giá, chữ thường và gạch dưới, ví dụ <c>sjc_bar</c>.</summary>
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    /// <summary>Các cách trang viết tên dòng. Lấy dòng đầu tiên trên trang ra được cặp giá hợp lệ.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];
}

public sealed record GoldUnit
{
    public string Text { get; init; } = string.Empty;
    public long Multiplier { get; init; }
}

public sealed record FuelReleaseName
{
    public string Name { get; init; } = string.Empty;
    public string FuelKey { get; init; } = string.Empty;
}

public sealed record LimitSettings
{
    /// <summary>
    /// Ngưỡng cảnh báo ngân sách MẶC ĐỊNH của hệ thống, tính theo % hạn mức đã dùng: T1 (nhắc nhẹ) và T2 (cảnh báo mạnh). Chỉ dùng cho người dùng KHÔNG có
    /// ngưỡng cá nhân và không đang dùng cách quản lý nào định nghĩa T1/T2; mỗi cách quản lý có T1/T2 riêng và người dùng có thể đặt ngưỡng cá nhân.
    /// Mức "vượt hạn mức" luôn là 100% nên không cấu hình ở đây. Đây là NƠI DUY NHẤT giữ hai con số mặc định (70, 90).
    /// </summary>
    public int AlertT1Percent { get; init; } = 70;
    public int AlertT2Percent { get; init; } = 90;
}

public sealed record SyncSettings
{
    public int IntervalMinutes { get; init; } = 15;
    public int MaxRetries { get; init; } = 5;
    public int QueueRetentionDays { get; init; } = 7;
}

/// <summary>Giao diện app. Giá trị <c>null</c> nghĩa là "dùng mặc định có sẵn trong app", nên cấu hình trống không đổi gì trên máy người dùng.</summary>
public sealed record ThemeSettings
{
    public string? PrimaryColor { get; init; }
    public string? BackgroundColor { get; init; }
    public string? NavBarColor { get; init; }

    /// <summary>Font có sẵn trong app, dùng khi <see cref="FontFamily"/> là null; luôn chọn được dù không nằm trong <see cref="AvailableFonts"/>.</summary>
    public const string DefaultFont = "Be Vietnam Pro";

    /// <summary>Tên font trên Google Fonts; null là font mặc định. Phải là <see cref="DefaultFont"/> hoặc nằm trong <see cref="AvailableFonts"/>. App không tải được thì dùng font mặc định.</summary>
    public string? FontFamily { get; init; }

    /// <summary>
    /// Các font (tên trên Google Fonts) admin được chọn cho app, ngoài font mặc định. Nằm trong cấu hình để thêm font không cần sửa code; app tải font theo tên
    /// lúc chạy nên cũng không cần phát hành lại. Mặc định là danh sách trang quản trị từng viết cứng.
    /// </summary>
    public IReadOnlyList<string> AvailableFonts { get; init; } = ["Inter", "Roboto", "Nunito", "Open Sans", "Lexend", "Montserrat"];

    /// <summary>Hệ số phóng cỡ chữ toàn app (1.0 = mặc định).</summary>
    public double TextScale { get; init; } = 1.0;
    public string? LogoUrl { get; init; }
}

/// <summary>
/// Quét QR chuyển khoản (VietQR). Các giá trị từng nằm cứng trong code: admin chỉnh được mà không cần phát hành lại, có lịch sử và khôi phục như mọi cấu hình khác. Mặc định ở đây là giá trị đã dùng khi chưa ai
/// lưu cấu hình, nên thêm nhóm này không đổi hành vi của bản cũ.
/// </summary>
public sealed record QrTransferSettings
{
    /// <summary>Thông báo ngân hàng phải đến trong vòng này SAU lúc quét thì mới khớp với lần quét. Quá hẹp thì lỡ người dùng nhập OTP chậm, quá rộng thì khớp nhầm giao dịch cùng số tiền.</summary>
    public int MatchWindowMinutes { get; init; } = 30;

    /// <summary>Đồng hồ điện thoại (gắn vào thông báo) và máy chủ (gắn vào lúc quét) có thể lệch: cho phép lần quét "sau" thông báo tối đa chừng này.</summary>
    public int ClockSkewMinutes { get; init; } = 2;

    /// <summary>Lần quét chưa khớp quá chừng này thì được đánh dấu hết hạn (chỉ để hiển thị).</summary>
    public int PendingLifetimeHours { get; init; } = 24;

    /// <summary>Trần số tiền một lần quét (đồng): chặn số rác, không phải hạn mức nghiệp vụ.</summary>
    public long MaxAmount { get; init; } = 100_000_000_000;

    /// <summary>Độ dài tối đa nội dung chuyển khoản. Cột DB rộng 100 nên không đặt quá 100.</summary>
    public int MaxMessageLength { get; init; } = 100;

    /// <summary>Nội dung chuyển khoản điền sẵn khi mã QR không có. Nên viết không dấu: nhiều ngân hàng không nhận dấu tiếng Việt.</summary>
    public string DefaultMessage { get; init; } = "Chuyen tien qua FinMate";

    /// <summary>Máy chủ chuyển tiếp mở app ngân hàng (chỉ tên miền, không kèm đường dẫn). Số tài khoản, số tiền và nội dung của người dùng đi qua máy chủ này nên chỉ đổi khi dịch vụ đổi địa chỉ.</summary>
    public string DeeplinkHost { get; init; } = "dl.vietqr.io";

    /// <summary>Tham số <c>url</c> của liên kết mở app ngân hàng. Thiếu nó thì app ngân hàng chỉ mở màn hình chủ.</summary>
    public string DeeplinkReturnUrl { get; init; } = "https://www.vietqr.io/deeplink";
}

/// <summary>
/// Cửa sổ xử lý Flow 1 (thông báo ngân hàng) — §5.5 (trùng nghiệp vụ) và §16.3 (gộp chuyển khoản nội bộ).
/// </summary>
public sealed record NotificationPipelineSettings
{
    /// <summary>Cửa sổ xét trùng nghiệp vụ THẬT (§5.5) quanh TransactedAt, tính bằng phút.</summary>
    public int BusinessDuplicateWindowMinutes { get; init; } = 5;

    /// <summary>Cửa sổ khớp 2 vế của 1 lần chuyển tiền nội bộ cùng user để gộp thành 1 Transfer (§16.3), tính bằng phút.</summary>
    public int SelfTransferMergeWindowMinutes { get; init; } = 10;
}

/// <summary>
/// Gợi ý cách quản lý tài chính. Hai hằng số từng nằm cứng trong code; mặc định là giá trị đã dùng nên thêm nhóm này không đổi hành vi của bản cũ.
/// Chỉ dẫn gửi cho AI nằm ở chỗ khác (<c>/admin/finance-method-prompts</c>) vì có phiên bản riêng để truy ngược từng gợi ý.
/// </summary>
public sealed record FinanceMethodSettings
{
    /// <summary>Trong khoảng này, xin gợi ý lại với CÙNG câu trả lời khảo sát thì trả bản vừa tạo, không gọi AI lại. 0 = luôn tạo mới.</summary>
    public int RecommendationReuseMinutes { get; init; } = 10;

    /// <summary>Khi chọn bằng luật (AI lỗi hoặc không dùng được), cách bổ trợ chỉ được chọn nếu điểm của nó ít nhất bằng chừng này phần trăm điểm của cách chính.</summary>
    public int SupportMinSharePercent { get; init; } = 50;

    /// <summary>
    /// Chữ cảnh báo của kế hoạch tháng theo mã (app tra theo mã, không viết cứng). <c>fixed_exceeds_group</c> có chỗ thay <c>{group}</c> bằng tên nhóm.
    /// </summary>
    public IReadOnlyDictionary<string, string> PlanWarningTexts { get; init; } = new Dictionary<string, string>
    {
        ["deficit"] = "Chi dự kiến đang vượt thu nhập. Trước hết hãy giảm bớt chi hoặc tìm thêm thu nhập để không bị hụt cuối tháng, rồi mới tính đến để dành.",
        ["below_save_target"] = "Phần dư chưa đạt mức để dành mà cách quản lý này đề xuất. Bạn có thể giảm bớt vài hạn mức để dư nhiều hơn.",
        ["fixed_exceeds_group"] = "Khoản cố định đã vượt phần dành cho nhóm \"{group}\", nên các khoản linh hoạt trong nhóm này tạm để 0.",
    };
}

/// <summary>Một khoảng ngày lễ/Tết mà chi tiêu linh hoạt thường tăng (hoặc giảm). Tết đổi ngày theo âm lịch nên admin nhập theo từng năm.</summary>
public sealed record ForecastHoliday
{
    public string Name { get; init; } = string.Empty;
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }

    /// <summary>Hệ số chi tiêu linh hoạt trong khoảng này, theo phần trăm (140 = chi nhiều hơn 40%).</summary>
    public int MultiplierPercent { get; init; } = 100;
}

/// <summary>
/// Dự báo tài chính (Flow 6). Các số ở đây thay cho những mã BR mà sơ đồ nhắc tới nhưng chưa có định nghĩa; mặc định do chủ dự án duyệt ngày 2026-11-02.
/// </summary>
public sealed record ForecastSettings
{
    /// <summary>Cửa sổ lịch sử dùng để học thói quen chi tiêu.</summary>
    public int HistoryWindowDays { get; init; } = 90;

    /// <summary>Đủ dữ liệu (BR-01.2, BR-06.1): ít nhất chừng này ngày có giao dịch trong cửa sổ lịch sử; ít hơn thì dự báo mang cờ tin cậy thấp.</summary>
    public int MinHistoryDays { get; init; } = 30;

    /// <summary>Dự phòng mặc định (BR-03.3) theo % thu nhập dự kiến tháng, khi user chưa tự đặt.</summary>
    public int ReservePercentOfIncome { get; init; } = 5;

    /// <summary>Dự báo dòng tiền xa nhất bao nhiêu ngày.</summary>
    public int MaxHorizonDays { get; init; } = 90;

    /// <summary>Số tiền các lần của cùng một khoản định kỳ được lệch nhau tối đa bao nhiêu %.</summary>
    public int AmountTolerancePercent { get; init; } = 10;

    public int MonthlyMinIntervalDays { get; init; } = 25;
    public int MonthlyMaxIntervalDays { get; init; } = 35;
    public int WeeklyMinIntervalDays { get; init; } = 6;
    public int WeeklyMaxIntervalDays { get; init; } = 8;

    /// <summary>Thấy chừng này lần lặp thì đề xuất khoản định kỳ chờ user xác nhận.</summary>
    public int SuggestMinOccurrences { get; init; } = 2;

    /// <summary>Thấy chừng này lần lặp thì tự ghi nhận (vẫn sửa/hủy được).</summary>
    public int AutoActivateOccurrences { get; init; } = 3;

    /// <summary>Khoản chi định kỳ quá hạn mà chưa thấy thanh toán vẫn được coi là sắp trả trong chừng này ngày; quá đó thì không tính nữa.</summary>
    public int OverdueGraceDays { get; init; } = 7;

    /// <summary>Số khoản giả định tối đa trong một lần thử kịch bản.</summary>
    public int MaxScenarioItems { get; init; } = 20;

    // ---- Đánh giá khoản mua ("Mua món này có ổn không?") ----

    /// <summary>Chưa biết kỳ thu nhập kế tiếp và người dùng không chọn mốc: đánh giá tới chừng này ngày sau ngày mua (kết quả chỉ ở mức một phần).</summary>
    public int PurchaseDefaultHorizonDays { get; init; } = 14;

    /// <summary>Số dư ví cập nhật cách đây quá chừng này ngày thì đánh giá chỉ ở mức một phần.</summary>
    public int PurchaseStaleBalanceDays { get; init; } = 7;

    public IReadOnlyList<ForecastHoliday> Holidays { get; init; } = [];

    // ---- Đợt 2: cảnh báo (duyệt 2026-11-02) ----

    /// <summary>Cảnh báo số dư thấp nhìn trước chừng này ngày.</summary>
    public int LowBalanceHorizonDays { get; init; } = 14;

    /// <summary>Đã gửi cảnh báo số dư thấp thì chừng này ngày sau mới gửi lại ("Đã gửi cảnh báo tương tự gần đây?").</summary>
    public int LowBalanceCooldownDays { get; init; } = 3;

    /// <summary>User báo một cảnh báo số dư thấp "không chính xác" thì tắt loại cảnh báo này chừng này ngày.</summary>
    public int InaccurateSuppressDays { get; init; } = 7;

    /// <summary>Vượt ngân sách sớm (BR-04.2): đã qua ít nhất chừng này ngày trong tháng thì dùng nhịp chi tháng này, ít hơn thì dùng thói quen 90 ngày.</summary>
    public int BudgetRiskMinElapsedDays { get; init; } = 7;

    /// <summary>Vượt ngân sách sớm chỉ báo khi còn ít nhất chừng này ngày để kịp điều chỉnh.</summary>
    public int BudgetRiskMinDaysRemaining { get; init; } = 3;

    /// <summary>Nhắc hóa đơn trước hạn chừng này ngày.</summary>
    public int BillReminderDaysBefore { get; init; } = 3;

    /// <summary>Thu nhập trễ (BR-07.4): quá ngày dự kiến chừng này ngày mà chưa thấy thì báo.</summary>
    public int IncomeLateDays { get; init; } = 3;

    /// <summary>Thu nhập không đều (BR-07.2) ước tính từ chừng này tháng gần nhất.</summary>
    public int IrregularIncomeMonths { get; init; } = 6;

    /// <summary>Giờ yên lặng mặc định (phút trong ngày, giờ VN) cho cảnh báo số dư thấp, khi user chưa đặt riêng.</summary>
    public int QuietHoursStartMinute { get; init; } = 22 * 60;
    public int QuietHoursEndMinute { get; init; } = 7 * 60;

    // ---- Đợt 3: bất thường, chi theo danh mục, mục tiêu (duyệt 2026-11-02) ----

    /// <summary>Bất thường: so với các khoản chi trong chừng này ngày gần nhất.</summary>
    public int AnomalyLookbackDays { get; init; } = 90;

    /// <summary>BR-11.1: cần ít nhất chừng này khoản chi cùng danh mục để so.</summary>
    public int AnomalyMinCategorySamples { get; init; } = 5;

    /// <summary>So theo người nhận cần ít nhất chừng này lần trả trước đó.</summary>
    public int AnomalyMinMerchantSamples { get; init; } = 3;

    /// <summary>BR-11.2: gấp ít nhất chừng này phần trăm mức thông thường (trung vị) mới là bất thường.</summary>
    public int AnomalyRatioPercent { get; init; } = 300;

    /// <summary>BR-11.3: gấp ít nhất chừng này phần trăm thì là mức cao (gửi thông báo).</summary>
    public int AnomalyHighRatioPercent { get; init; } = 500;

    /// <summary>Khoản dưới số tiền này không bao giờ bị coi là bất thường (tránh nhiễu với khoản nhỏ).</summary>
    public long AnomalyMinAmountCents { get; init; } = 200_000;

    /// <summary>Hai khoản cùng ví, cùng số tiền, cùng nơi nhận trong chừng này phút → nghi trùng.</summary>
    public int DuplicateWindowMinutes { get; init; } = 10;

    /// <summary>BR-11.4: đã xác nhận bình thường một khoản thì khoản tương tự có số tiền tới (khoản đó × chừng này %) không bị báo nữa.</summary>
    public int NormalFeedbackTolerancePercent { get; init; } = 125;

    /// <summary>Quét bất thường các khoản chi trong chừng này ngày gần nhất.</summary>
    public int AnomalyScanDays { get; init; } = 3;

    /// <summary>Dự báo theo danh mục dùng chừng này tháng trọn vẹn gần nhất.</summary>
    public int CategoryForecastMonths { get; init; } = 3;

    /// <summary>BR-06.1: cần dữ liệu ở ít nhất chừng này tháng trọn vẹn, ít hơn → độ tin cậy thấp.</summary>
    public int CategoryForecastMinMonths { get; init; } = 2;

    /// <summary>Danh mục "thay đổi nhiều" khi dự kiến lệch ít nhất chừng này phần trăm so với tháng trước.</summary>
    public int CategoryChangePercent { get; init; } = 20;

    /// <summary>Hạn mức đề xuất làm tròn lên bội số này (đồng).</summary>
    public long BudgetSuggestionRoundingCents { get; init; } = 10_000;

    /// <summary>BR-08.1: tốc độ góp mục tiêu tính từ tối đa chừng này tháng gần nhất.</summary>
    public int GoalRateMonths { get; init; } = 6;

    /// <summary>BR-LH-08 (Phase 84): độ phủ dữ liệu từ mức này trở lên là <c>good</c>.</summary>
    public int GoodCoveragePercent { get; init; } = 85;

    /// <summary>BR-LH-08: độ phủ từ mức này tới dưới <see cref="GoodCoveragePercent"/> là <c>partial</c>; thấp hơn là <c>poor</c> (độ tin cậy thấp kèm câu cảnh báo).</summary>
    public int MinCoveragePercent { get; init; } = 60;
}

/// <summary>
/// Nguồn số liệu kinh tế tự cập nhật (giả định lạm phát hằng tháng). Mặc định TẮT: <see cref="CpiIndexUrl"/> rỗng thì job không chạy.
/// Địa chỉ nằm ở cấu hình admin chứ không trong mã, vì trang nguồn có thể đổi.
/// </summary>
public sealed record EconomicFeedsSettings
{
    /// <summary>Bật/tắt lịch tự chạy. Tắt thì gỡ job khỏi lịch (nút "Chạy ngay" vẫn dùng được).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Ngày trong tháng job chạy (1–28, tránh tháng ngắn). Cục Thống kê công bố CPI khoảng ngày 3–6.</summary>
    public int ScheduleDayOfMonth { get; init; } = 6;

    /// <summary>Giờ chạy (0–23, giờ Việt Nam).</summary>
    public int ScheduleHour { get; init; } = 9;

    /// <summary>Trang danh sách thông cáo của Cục Thống kê (https). Rỗng = không tải gì.</summary>
    public string CpiIndexUrl { get; init; } = string.Empty;

    /// <summary>Bản CPI mới chỉ tự duyệt khi chênh so với bản đang dùng không quá số điểm cơ bản này (100 = 1 điểm phần trăm); lớn hơn thì để Draft chờ người duyệt.</summary>
    public int AutoApproveMaxDeltaBps { get; init; } = 100;

    /// <summary>Khoảng hợp lý của tốc độ tăng hằng năm (điểm cơ bản). Ngoài khoảng này (ví dụ đọc nhầm 45,7% thay vì 4,57%) thì không bao giờ tự duyệt.</summary>
    public int MinRateBps { get; init; } = -2000;

    public int MaxRateBps { get; init; } = 5000;
}

/// <summary>AI Financial Agent.</summary>
public sealed record AgentSettings
{
    /// <summary>
    /// Mẫu chia ngân sách của công cụ <c>plan_budget</c>, dùng khi người dùng chưa có chi tiêu 3 tháng trọn gần nhất: chuỗi JSON <c>{"slug": phần trăm nguyên}</c>, tổng đúng 100.
    /// Mặc định RỖNG (chủ dự án chốt 2026-11-08: không đặt tỉ lệ sẵn trong code, admin nhập). Sai dạng thì công cụ coi như chưa có mẫu.
    /// </summary>
    public string BudgetTemplate { get; init; } = string.Empty;

    /// <summary>
    /// Khóa tham số của cách quản lý (<c>FinanceMethod.ParamDefs</c>) được tính là phần TIẾT KIỆM của thu nhập. Công cụ <c>goal_plan</c> cộng giá trị đang có hiệu lực của các khóa này ở cách chính người dùng đang dùng
    /// (đã chọn từ khảo sát đầu vào) để ước tính sơ khảo "mỗi tháng dành được bao nhiêu" khi chưa có chi tiêu thật. Mặc định là các khóa của chín cách và bốn cách phổ biến đã seed (50/30/20, để dành trước, 6 hũ...);
    /// admin thêm khóa khi tạo cách mới có phần tiết kiệm. Cách không có khóa nào trong danh sách (vd chỉ theo dõi) thì không ước tính được.
    /// </summary>
    public IReadOnlyList<string> SavingParamKeys { get; init; } = ["goals_pct", "saving_pct", "contribution_pct", "long_term_pct", "financial_freedom_pct"];

    /// <summary>
    /// Mục tiêu chưa khả thi thì <c>goal_plan</c> thử "giảm bấy nhiêu phần trăm chi của nhóm chi nhiều nhất" để xem rút ngắn được bao lâu (kèm thẻ đặt hạn mức cho nhóm đó).
    /// Đây là mức giảm giả định để so sánh, không phải lời khuyên cắt bao nhiêu; admin chỉnh trong 1–50, 0 = tắt gợi ý này.
    /// </summary>
    public int CutSuggestionPercent { get; init; } = 10;

    /// <summary>
    /// Ước tính sơ khảo "mỗi tháng dành được bao nhiêu % thu nhập" từ câu trả lời khảo sát ban đầu khi chưa có chi tiêu thật và cách quản lý đã chọn không có phần tiết kiệm:
    /// khóa <c>"mãCâuHỏi:mãĐápÁn"</c> → phần trăm 0–100 (vd <c>"month_end_balance:C"</c>). Nhiều câu có mức thì lấy mức thấp nhất. Mặc định RỖNG: admin nhập, code không giữ tỉ lệ nào.
    /// </summary>
    public IReadOnlyDictionary<string, int> SurveySavingShare { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Mức chi trung bình mỗi tháng (đồng) ứng với đáp án của câu khảo sát <c>avg_monthly_spend</c>: khóa <c>"avg_monthly_spend:B"</c> → số đồng (thường là điểm giữa của khoảng).
    /// Dùng khi chưa có chi tiêu thật: khả năng góp = thu nhập khai − mức này. Ưu tiên hơn phần trăm theo cách quản lý và theo <see cref="SurveySavingShare"/>. Mặc định RỖNG: admin nhập.
    /// </summary>
    public IReadOnlyDictionary<string, long> SurveySpendEstimate { get; init; } = new Dictionary<string, long>();

    /// <summary>
    /// Giá token vào của mô hình LLM, USD cho 1 triệu token, theo tên mô hình (khóa trùng <c>AGENT_MODEL</c> của AI Service, vd <c>gemini-3.1-flash-lite</c>). Trang admin "Token và chi phí AI" nhân giá này với số token.
    /// Mặc định RỖNG: admin nhập từ trang giá của nhà cung cấp; thiếu giá thì trang chỉ hiện token, không đoán tiền.
    /// </summary>
    public IReadOnlyDictionary<string, double> LlmInputUsdPerMillion { get; init; } = new Dictionary<string, double>();

    /// <summary>Giá token ra tương ứng, cùng khóa và cùng đơn vị với <see cref="LlmInputUsdPerMillion"/>.</summary>
    public IReadOnlyDictionary<string, double> LlmOutputUsdPerMillion { get; init; } = new Dictionary<string, double>();

    /// <summary>
    /// Mẫu câu cố định của công cụ agent (khóa → mẫu có chỗ trống <c>{tên}</c>), xem <c>AgentTexts.Catalog</c>. Chỉ cần ghi các mẫu muốn đổi; khóa vắng mặt dùng mẫu mặc định.
    /// </summary>
    public IReadOnlyDictionary<string, string> Texts { get; init; } = new Dictionary<string, string>();
}

/// <summary>Một màu nhận diện trong bảng màu danh mục: <paramref name="Key"/> ổn định để tham chiếu, <paramref name="Hex"/> dạng <c>#RRGGBB</c>, <paramref name="Name"/> hiện cho người dùng.</summary>
public sealed record CategoryColor(string Key, string Hex, string Name);

/// <summary>Danh mục: bảng màu pastel của màn "Thêm danh mục mới" (Figma H13). Admin đổi được mà không phát hành lại app.</summary>
public sealed record CategorySettings
{
    public IReadOnlyList<CategoryColor> Palette { get; init; } =
    [
        new("coral", "#F4A5A5", "Hồng san hô"),
        new("honey", "#F0C8A0", "Cam mật ong"),
        new("sky", "#8FB3E0", "Xanh trời"),
        new("blush", "#E5AFC4", "Hồng phấn"),
        new("mint", "#8EDDB4", "Xanh bạc hà"),
        new("lavender", "#D5CBF5", "Tím oải hương"),
        new("navy", "#4B5F8F", "Xanh navy"),
        new("butter", "#F5E27E", "Vàng bơ"),
    ];
}

/// <summary>Một câu gợi ý trong khung chat. <paramref name="When"/>: <c>always</c> | <c>noTransactionToday</c> | <c>hasBudget</c> | <c>hasGoal</c> (xem <see cref="AssistantSettings.SuggestionContexts"/>).</summary>
public sealed record AssistantSuggestion(string Key, string Text, string When = "always");

/// <summary>Trợ lý chat: câu gợi ý theo ngữ cảnh và độ tin cậy gán cho danh mục theo nguồn AI dùng. Admin đổi được mà không phát hành lại app.</summary>
public sealed record AssistantSettings
{
    public static readonly IReadOnlyList<string> SuggestionContexts = ["always", "noTransactionToday", "hasBudget", "hasGoal"];

    /// <summary>Tối đa bao nhiêu câu trả về một lần.</summary>
    public int MaxSuggestions { get; init; } = 4;

    public IReadOnlyList<AssistantSuggestion> Suggestions { get; init; } =
    [
        new("add_coffee", "Hôm nay mình uống cà phê 35k", "noTransactionToday"),
        new("spent_month", "Tháng này mình đã chi bao nhiêu?"),
        new("spent_today", "Chi tiêu hôm nay của mình"),
        new("budget_left", "Ngân sách còn lại bao nhiêu?", "hasBudget"),
        new("goal_progress", "Mục tiêu tiết kiệm của mình đến đâu rồi?", "hasGoal"),
    ];

    /// <summary>
    /// Độ tin cậy (0–1) của danh mục theo <c>categorySource</c> mà AI Service trả (memory = người dùng từng chốt, brand, keyword, product, model).
    /// Chat không có điểm số thật từ AI nên đây là bảng quy đổi do admin chỉnh; nguồn không có trong bảng thì không báo độ tin cậy.
    /// </summary>
    public IReadOnlyDictionary<string, double> CategorySourceConfidence { get; init; } = new Dictionary<string, double>
    {
        ["memory"] = 0.95,
        ["brand"] = 0.9,
        ["keyword"] = 0.85,
        ["product"] = 0.8,
        ["model"] = 0.7,
    };
}

/// <summary>
/// Hoàn tiền / đảo giao dịch (Phase 81). Các cửa sổ chỉ giới hạn danh sách GỢI Ý khoản chi gốc cho một khoản tiền vào mà AI đoán là hoàn/đảo;
/// người dùng liên kết tay thì không giới hạn thời gian (D12).
/// </summary>
public sealed record RefundSettings
{
    /// <summary>Gợi ý khoản chi trong bao nhiêu ngày trước khoản hoàn.</summary>
    public int MatchWindowDays { get; init; } = 60;

    /// <summary>Đảo giao dịch lỗi thường về trong vài ngày, nên cửa sổ ngắn hơn hẳn.</summary>
    public int ReversalMatchWindowDays { get; init; } = 3;

    /// <summary>Số khoản chi gợi ý tối đa.</summary>
    public int MaxSuggestions { get; init; } = 3;
}

/// <summary>
/// Cửa sổ thời gian để coi hai giao dịch cùng chiều, cùng số tiền là có thể trùng (Phase 82, BR-DUP-03), theo cặp nguồn.
/// Hai khoản đều nhập tay thì không bao giờ so: user tự nhập hai lần là ý của họ.
/// </summary>
public sealed record DuplicateSettings
{
    /// <summary>Thông báo ↔ thông báo. Cũng là cửa sổ để hai thông báo cùng số dư sau giao dịch được coi là cùng một sự kiện (BR-DUP-05).</summary>
    public int NotificationWindowMinutes { get; init; } = 10;

    /// <summary>Nhập tay, giọng nói, nhân viên hỗ trợ ↔ thông báo. Dài vì người dùng hay ghi lại khoản chi muộn.</summary>
    public int ManualWindowHours { get; init; } = 48;

    /// <summary>Hoá đơn ↔ thông báo, và hoá đơn ↔ nhập tay (chỉ để cảnh báo, D4).</summary>
    public int ReceiptWindowHours { get; init; } = 12;
}

/// <summary>
/// Ghép cặp chuyển tiền giữa các ví của chính người dùng (Phase 83, BR-TRF-02). Chỉ là điều kiện GỢI Ý; người dùng mới là người chuyển đổi.
/// </summary>
public sealed record TransferSettings
{
    /// <summary>Tiền vào phải đến sau tiền ra tối đa bấy nhiêu phút.</summary>
    public int PairWindowMinutes { get; init; } = 30;

    /// <summary>Hai push có thể về lệch nhau: tiền vào được phép đến SỚM hơn tiền ra tối đa bấy nhiêu phút (D6).</summary>
    public int ClockSkewMinutes { get; init; } = 2;

    /// <summary>Phần chênh tối đa (đồng) giữa tiền ra và tiền vào để vẫn coi là một lần chuyển có phí.</summary>
    public long MaxFeeCents { get; init; } = 20_000;

    /// <summary>Phần chênh tối đa tính theo % số tiền chuyển; phải thoả CẢ hai giới hạn.</summary>
    public int MaxFeePercent { get; init; } = 2;

    /// <summary>
    /// Bí danh của từng provider (khoá = <c>provider_key</c>) để nhận ra "nội dung nhắc tên ví bên kia" (D5). Tên hiển thị và chính
    /// <c>provider_key</c> luôn được dùng thêm, không cần khai ở đây.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> ProviderAliases { get; init; } = new Dictionary<string, string[]>
    {
        ["vietcombank"] = ["vcb", "vietcombank"],
        ["momo"] = ["momo"],
        ["zalopay"] = ["zalopay", "zalo pay"],
        ["vnpay"] = ["vnpay"],
        ["shopeepay"] = ["shopeepay", "shopee pay"],
        ["techcombank"] = ["tcb", "techcombank"],
        ["vpbank"] = ["vpbank", "vpb"],
        ["mb_bank"] = ["mb bank", "mbbank"],
        ["vietinbank"] = ["vietinbank", "ctg"],
        ["bidv"] = ["bidv"],
    };
}

/// <summary>Ảnh chụp lúc gộp / chuyển đổi (Phase 83, phần B, S5): giữ đủ lâu để hoàn tác, không lâu hơn (tối thiểu hoá dữ liệu).</summary>
public sealed record SnapshotSettings
{
    public int RetentionDays { get; init; } = 365;
}

/// <summary>
/// Sức khoẻ dịch vụ đọc thông báo và độ phủ dữ liệu (Phase 84, BR-LH-*). Mặc định do chủ dự án duyệt ngày 2026-11-14.
/// </summary>
public sealed record ListenerHealthSettings
{
    /// <summary>BR-LH-02: giữ dòng trạng thái theo ngày của thiết bị (và số ca <c>upload_gap</c>) chừng này ngày rồi xoá.</summary>
    public int HistoryRetentionDays { get; init; } = 90;

    /// <summary>BR-LH-03: cửa sổ lịch sử thông báo của một ví dùng để ước lượng khoảng im lặng bình thường.</summary>
    public int HistoryDays { get; init; } = 30;

    /// <summary>BR-LH-03: ít hơn chừng này thông báo trong cửa sổ thì ví không bị xét (ví ít dùng, im lặng là bình thường).</summary>
    public int MinNotificationsForBaseline { get; init; } = 8;

    /// <summary>BR-LH-03: khoảng im lặng cho phép không bao giờ ngắn hơn chừng này giờ.</summary>
    public int MinSilenceHours { get; init; } = 48;

    /// <summary>BR-LH-03: khoảng im lặng cho phép = hệ số này × phân vị 90 của khoảng cách giữa hai thông báo liên tiếp (nếu lớn hơn sàn).</summary>
    public double SilenceMultiplier { get; init; } = 3;

    /// <summary>BR-LH-04: thiết bị không báo trạng thái quá chừng này giờ thì là <c>unknown</c>.</summary>
    public int StaleReportHours { get; init; } = 72;

    /// <summary>S4: một ngày không có lần báo nào kế thừa trạng thái của lần báo gần nhất nếu lần đó cách không quá chừng này giờ.</summary>
    public int CoverageInheritHours { get; init; } = 36;

    /// <summary>BR-LH-05: mỗi ví/thiết bị nhắc cách nhau ít nhất chừng này ngày lịch (giờ VN).</summary>
    public int CooldownDays { get; init; } = 3;

    /// <summary>BR-LH-05: tối đa chừng này lần nhắc cho một đợt im lặng / mất quyền.</summary>
    public int MaxRemindersPerEpisode { get; init; } = 3;

    /// <summary>D1: mỗi user báo trạng thái cho tối đa chừng này thiết bị.</summary>
    public int MaxDevicesPerUser { get; init; } = 10;

    /// <summary>BR-LH-10: tỉ lệ đọc lỗi 24 giờ gấp ít nhất chừng này lần 7 ngày trước đó thì nghi ngân hàng đổi mẫu tin.</summary>
    public double FormatDriftRatio { get; init; } = 3;

    /// <summary>BR-LH-10: cần ít nhất chừng này thông báo trong 24 giờ mới xét.</summary>
    public int FormatDriftMinSamples { get; init; } = 20;

    /// <summary>D14: tỉ lệ đọc lỗi 24 giờ phải ít nhất chừng này phần trăm, để một lỗi lẻ không mở sự cố.</summary>
    public int FormatDriftMinRatePercent { get; init; } = 10;

    /// <summary>D14: tỉ lệ nền dùng để so không thấp hơn chừng này phần trăm (nền 0% không cho tỉ số vô cực).</summary>
    public int FormatDriftBaselineFloorPercent { get; init; } = 1;

    /// <summary>D15: số lượt kiểm (mỗi giờ một lượt) liên tiếp bình thường để tự đóng sự cố.</summary>
    public int FormatDriftRecoveryRuns { get; init; } = 24;

    /// <summary>S1: tỉ lệ thông báo <c>Financial</c> 24 giờ tụt dưới chừng này lần so với 7 ngày trước đó cũng là dấu hiệu đổi mẫu tin.</summary>
    public double FormatFinancialShareDropRatio { get; init; } = 0.33;
}

/// <summary>Sổ vay (Phase 90): gợi ý gắn giao dịch, nhắc hạn, trả kèm lãi (S1), khớp tên (Q15).</summary>
public sealed record LoanSettings
{
    /// <summary>Giao dịch đã xác nhận còn được gợi ý gắn khoản vay trong chừng này giờ từ lúc tạo (D9). ×10 là cửa sổ "khoản loan chưa gắn" (S5).</summary>
    public int SuggestWindowHours { get; init; } = 72;

    public int MaxSuggestions { get; init; } = 3;

    public int ReminderDaysBefore { get; init; } = 3;

    public int OverdueRepeatDays { get; init; } = 7;

    public int MaxOverdueReminders { get; init; } = 3;

    /// <summary>S1: trả vượt gốc còn lại được nhận khi phần vượt ≤ lãi tạm tính + chừng này (đồng); phần vượt thành giao dịch lãi.</summary>
    public long InterestToleranceCents { get; init; } = 10_000;

    /// <summary>Q15: từ xưng hô đứng đầu tên khoản, bỏ khi tìm tên trong nội dung thông báo.</summary>
    public string[] Honorifics { get; init; } = ["bạn", "anh", "chị", "em", "cô", "chú", "bác", "ông", "bà", "dì", "cậu"];
}

/// <summary>Xuất dữ liệu (Phase 91, BR-EXP-01…10, S3, S4). Mặc định do chủ sản phẩm duyệt ngày 2026-11-25.</summary>
public sealed record DataExportSettings
{
    /// <summary>BR-EXP-01: khoảng ngày tối đa của một file CSV giao dịch.</summary>
    public int MaxRangeDays { get; init; } = 366;

    /// <summary>BR-EXP-01: số dòng tối đa của một file CSV giao dịch.</summary>
    public int MaxRows { get; init; } = 50_000;

    /// <summary>S3: số file CSV mỗi user trong 60 phút.</summary>
    public int CsvPerHour { get; init; } = 20;

    /// <summary>BR-EXP-06: số yêu cầu gói đầy đủ trong 24 giờ.</summary>
    public int MaxRequestsPerDay { get; init; } = 1;

    /// <summary>BR-EXP-09: giữ file chừng này giờ sau khi xong rồi xoá.</summary>
    public int FileRetentionHours { get; init; } = 72;

    /// <summary>BR-EXP-09, S4: gói lớn hơn thì job thất bại và mở sự cố Normal.</summary>
    public int MaxArchiveMb { get; init; } = 25;

    /// <summary>BR-EXP-10: link tải dùng một lần sống chừng này phút.</summary>
    public int DownloadLinkMinutes { get; init; } = 10;

    /// <summary>Giữ dòng lịch sử yêu cầu (không có file) chừng này ngày.</summary>
    public int RequestHistoryDays { get; init; } = 365;
}

/// <summary>Dashboard KPI sản phẩm (Phase 94, BR-KPI-07). Không đưa ra <c>GET /app-config</c>.</summary>
public sealed record KpiSettings
{
    /// <summary>Ô của một nhóm con (nguồn giao dịch, gói thuê bao) nhỏ hơn số này thì trả <c>"&lt;n"</c> thay vì số chính xác. Tổng toàn hệ thống không che.</summary>
    public int MinCellSize { get; init; } = 5;
}

/// <summary>Toàn bộ cấu hình hệ thống không bí mật. Giá trị mặc định ở đây là cấu hình khi chưa ai lưu (version 0).</summary>
public sealed record SystemConfigDocument
{
    public MaintenanceSettings Maintenance { get; init; } = new();
    public AppVersionSettings AppVersion { get; init; } = new();
    public FeatureSettings Features { get; init; } = new();
    public LimitSettings Limits { get; init; } = new();
    public SyncSettings Sync { get; init; } = new();
    public ThemeSettings Theme { get; init; } = new();
    public QrTransferSettings QrTransfer { get; init; } = new();
    public NotificationPipelineSettings NotificationPipeline { get; init; } = new();
    public FinanceMethodSettings FinanceMethods { get; init; } = new();
    public ForecastSettings Forecast { get; init; } = new();
    public GroupSettings Groups { get; init; } = new();
    public SupportSettings Support { get; init; } = new();
    public MonitoringSettings Monitoring { get; init; } = new();
    public OpsNotificationSettings OpsNotifications { get; init; } = new();
    public AgentSettings Agent { get; init; } = new();
    public EconomicFeedsSettings EconomicFeeds { get; init; } = new();
    public MarketDataSettings MarketData { get; init; } = new();
    public CommunitySettings Community { get; init; } = new();
    public CategorySettings Categories { get; init; } = new();
    public AssistantSettings Assistant { get; init; } = new();
    public RefundSettings Refunds { get; init; } = new();
    public DuplicateSettings Duplicates { get; init; } = new();
    public TransferSettings Transfers { get; init; } = new();
    public SnapshotSettings Snapshots { get; init; } = new();
    public ListenerHealthSettings ListenerHealth { get; init; } = new();
    public LoanSettings Loans { get; init; } = new();
    public DataExportSettings DataExport { get; init; } = new();
    public KpiSettings Kpi { get; init; } = new();
}

/// <summary>Cấu hình đang chạy kèm số phiên bản (0 = chưa ai lưu, đang dùng giá trị mặc định).</summary>
public sealed record SystemConfigSnapshot(int Version, SystemConfigDocument Document, DateTimeOffset? UpdatedAt);
