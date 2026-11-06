using System.Text.RegularExpressions;
using FluentValidation;

namespace FinMate.Application.Admin.SystemConfig;

/// <summary>Chủ đề của cộng đồng. <see cref="RequiresPlace"/> quyết định bài có gắn quán và khu vực hay không.</summary>
public sealed record CommunityTopic
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool RequiresPlace { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record CommunityArea
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>Khu vực là tên người dân quen gọi, không phải đơn vị hành chính: từ 01/07/2025 cấp quận không còn, nhưng mọi người vẫn nói "Quận 7".</summary>
public sealed record CommunityProvince
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<CommunityArea> Areas { get; init; } = [];
}

/// <summary>Danh sách từ khóa chỉ là điểm xuất phát, cần đo lại trên dữ liệu thật.</summary>
public sealed record CommunitySettings
{
    public const string PlacesTopic = "places";

    /// <summary>Thay thế phần này trong <c>GET /app-config</c>, là API ẩn danh: để lộ từ cấm thì người xấu biết cách né. Chủ đề và khu vực có API riêng.</summary>
    public static readonly CommunitySettings HiddenFromPublic = new()
    {
        Topics = [],
        Regions = [],
        FoodKeywords = [],
        FoodExcludes = [],
        BannedWords = [],
    };

    public IReadOnlyList<CommunityTopic> Topics { get; init; } = CommunityDefaults.Topics;
    public IReadOnlyList<CommunityProvince> Regions { get; init; } = CommunityDefaults.Regions;
    public IReadOnlyList<string> FoodKeywords { get; init; } = CommunityDefaults.FoodKeywords;
    public IReadOnlyList<string> FoodExcludes { get; init; } = CommunityDefaults.FoodExcludes;

    /// <summary>So khớp giữ dấu: bỏ dấu thì "các" trùng với từ tục, và người ta vẫn phải nói được về "vay" và "nợ".</summary>
    public IReadOnlyList<string> BannedWords { get; init; } = CommunityDefaults.BannedWords;
    public int MaxPostsPerDay { get; init; } = 5;
    public int MaxCommentsPerDay { get; init; } = 30;
    public int MaxImagesPerDay { get; init; } = 10;
    public int MaxPendingImages { get; init; } = 500;
    public int MaxVotesPerHour { get; init; } = 60;
    public int MaxReportsPerDay { get; init; } = 20;
    public int AutoHideReportCount { get; init; } = 3;
    public long PromptMaxAmountCents { get; init; } = 300_000;
    public int PromptPerPlaceCooldownDays { get; init; } = 30;
    public int VerifiedLookbackDays { get; init; } = 30;
}

public sealed class CommunitySettingsValidator : AbstractValidator<CommunitySettings>
{
    private static readonly Regex Code = new("^[a-z0-9_-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public CommunitySettingsValidator()
    {
        RuleFor(x => x.Topics).Must(ValidTopics).WithMessage("Chủ đề phải có mã hợp lệ, không trùng, có tên, và luôn giữ chủ đề places gắn quán.");
        RuleFor(x => x.Regions).Must(ValidRegions).WithMessage("Khu vực phải có mã hợp lệ, không trùng và có tên.");
        RuleFor(x => x.FoodKeywords).Must(ValidWords).WithMessage("Từ khóa ăn uống không được để trống, tối đa 200 cụm, mỗi cụm tối đa 50 ký tự.");
        RuleFor(x => x.FoodExcludes).Must(ValidWords).WithMessage("Cụm loại trừ ăn uống không được để trống, tối đa 200 cụm, mỗi cụm tối đa 50 ký tự.");
        RuleFor(x => x.BannedWords).Must(ValidWords).WithMessage("Từ cấm không được để trống, tối đa 200 cụm, mỗi cụm tối đa 50 ký tự.");
        RuleFor(x => x.MaxPostsPerDay).InclusiveBetween(1, 100).WithMessage("Số bài mỗi ngày phải từ 1 đến 100.");
        RuleFor(x => x.MaxCommentsPerDay).InclusiveBetween(1, 500).WithMessage("Số bình luận mỗi ngày phải từ 1 đến 500.");
        RuleFor(x => x.MaxImagesPerDay).InclusiveBetween(1, 100).WithMessage("Số ảnh mỗi ngày phải từ 1 đến 100.");
        RuleFor(x => x.MaxPendingImages).InclusiveBetween(1, 100_000).WithMessage("Số ảnh chờ duyệt phải từ 1 đến 100000.");
        RuleFor(x => x.MaxVotesPerHour).InclusiveBetween(1, 1000).WithMessage("Số vote mỗi giờ phải từ 1 đến 1000.");
        RuleFor(x => x.MaxReportsPerDay).InclusiveBetween(1, 500).WithMessage("Số báo cáo mỗi ngày phải từ 1 đến 500.");
        RuleFor(x => x.AutoHideReportCount).InclusiveBetween(2, 100).WithMessage("Số báo cáo để tự ẩn phải từ 2 đến 100.");
        RuleFor(x => x.PromptMaxAmountCents).InclusiveBetween(1, 100_000_000).WithMessage("Mức tiền hỏi chia sẻ phải từ 1 đến 100.000.000.");
        RuleFor(x => x.PromptPerPlaceCooldownDays).InclusiveBetween(1, 365).WithMessage("Thời gian giãn cách phải từ 1 đến 365 ngày.");
        RuleFor(x => x.VerifiedLookbackDays).InclusiveBetween(1, 365).WithMessage("Thời gian xét đã chi phải từ 1 đến 365 ngày.");
    }

    private static bool ValidCode(string? code) => code is not null && Code.IsMatch(code);

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 100;

    private static bool ValidWords(IReadOnlyList<string>? words)
        => words is { Count: <= 200 } && words.All(word => !string.IsNullOrWhiteSpace(word) && word.Length <= 50);

    private static bool ValidTopics(IReadOnlyList<CommunityTopic>? topics)
    {
        if (topics is not { Count: > 0 and <= 30 } || topics.Any(t => t is null))
        {
            return false;
        }

        return topics.All(t => ValidCode(t.Code) && ValidName(t.Name) && t.Tags is { Count: <= 30 } && t.Tags.All(ValidCode))
            && topics.Select(t => t.Code).Distinct().Count() == topics.Count
            && topics.Any(t => t.Code == CommunitySettings.PlacesTopic && t.RequiresPlace);
    }

    private static bool ValidRegions(IReadOnlyList<CommunityProvince>? regions)
    {
        if (regions is not { Count: > 0 and <= 100 } || regions.Any(r => r is null))
        {
            return false;
        }

        return regions.All(ValidProvince) && regions.Select(r => r.Code).Distinct().Count() == regions.Count;
    }

    private static bool ValidProvince(CommunityProvince province)
        => ValidCode(province.Code)
            && ValidName(province.Name)
            && province.Areas is { Count: <= 200 }
            && province.Areas.All(a => a is not null && ValidCode(a.Code) && ValidName(a.Name))
            && province.Areas.Select(a => a.Code).Distinct().Count() == province.Areas.Count;
}
