using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FluentValidation;

namespace FinMate.Application.ListenerHealth;

/// <param name="LastPostedAt">Lần cuối listener thấy BẤT KỲ thông báo nào của app đó, kể cả thông báo bị lọc trên máy.</param>
public sealed record ListenerPackageReport(string? PackageName, bool? NotificationsEnabled, DateTimeOffset? LastPostedAt);

/// <summary>BR-LH-01. Hai cờ là <c>bool?</c> để body thiếu trường bị từ chối, thay vì im lặng hiểu thành "mất quyền".</summary>
public sealed record ReportListenerStatusCommand(
    Guid UserId,
    Guid? InstallationId,
    bool? PermissionGranted,
    bool? ListenerConnected,
    IReadOnlyList<ListenerPackageReport>? SelectedPackages,
    string? AppVersion,
    string? Manufacturer);

public interface IReportListenerStatusCommandHandler
{
    Task HandleAsync(ReportListenerStatusCommand command, CancellationToken ct = default);
}

public class ReportListenerStatusCommandValidator : AbstractValidator<ReportListenerStatusCommand>
{
    public const int MaxPackages = 50;
    public const int MaxPackageNameLength = 255;
    public const int MaxAppVersionLength = 32;
    public const int MaxManufacturerLength = 64;

    public ReportListenerStatusCommandValidator()
    {
        RuleFor(c => c.InstallationId).NotNull().NotEqual(Guid.Empty).WithMessage("Thiếu mã cài đặt của thiết bị.");
        RuleFor(c => c.PermissionGranted).NotNull().WithMessage("Thiếu trạng thái quyền đọc thông báo.");
        RuleFor(c => c.ListenerConnected).NotNull().WithMessage("Thiếu trạng thái kết nối của dịch vụ đọc thông báo.");
        RuleFor(c => c.SelectedPackages).Must(p => p is null || p.Count <= MaxPackages).WithMessage($"Tối đa {MaxPackages} ứng dụng ngân hàng/ví.");
        RuleForEach(c => c.SelectedPackages).ChildRules(p =>
            p.RuleFor(x => x.PackageName)
                .NotEmpty().WithMessage("Tên gói ứng dụng không được để trống.")
                .MaximumLength(MaxPackageNameLength).WithMessage("Tên gói ứng dụng quá dài.")
                .Must(n => n is null || !n.Any(char.IsControl)).WithMessage("Tên gói ứng dụng không hợp lệ."));
        RuleFor(c => c.AppVersion).MaximumLength(MaxAppVersionLength).Must(NoControl).WithMessage("Phiên bản ứng dụng không hợp lệ.");
        RuleFor(c => c.Manufacturer).MaximumLength(MaxManufacturerLength).Must(NoControl).WithMessage("Tên hãng máy không hợp lệ.");
    }

    private static bool NoControl(string? value) => value is null || !value.Any(char.IsControl);
}

/// <summary>
/// Ghi trạng thái listener mà app báo lên (BR-LH-01/02). Package lạ bị bỏ lặng lẽ, không lưu và không log (D2); <c>lastPostedAt</c> ở tương lai
/// quá 10 phút bị kẹp về giờ server (D5). Xong thì xoá cache độ phủ của user (S6).
/// </summary>
public class ReportListenerStatusCommandHandler : IReportListenerStatusCommandHandler
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly IListenerHealthRepository _repository;
    private readonly ISystemConfigProvider _config;
    private readonly ICacheService _cache;
    private readonly IValidator<ReportListenerStatusCommand> _validator;
    private readonly Func<DateTimeOffset> _now;

    public ReportListenerStatusCommandHandler(
        IListenerHealthRepository repository, ISystemConfigProvider config, ICacheService cache, IValidator<ReportListenerStatusCommand> validator)
        : this(repository, config, cache, validator, () => DateTimeOffset.UtcNow)
    {
    }

    internal ReportListenerStatusCommandHandler(
        IListenerHealthRepository repository, ISystemConfigProvider config, ICacheService cache, IValidator<ReportListenerStatusCommand> validator,
        Func<DateTimeOffset> now)
    {
        _repository = repository;
        _config = config;
        _cache = cache;
        _validator = validator;
        _now = now;
    }

    public async Task HandleAsync(ReportListenerStatusCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var document = (await _config.GetAsync(ct)).Document;
        var installation = command.InstallationId!.Value;

        if (!await _repository.DeviceExistsAsync(command.UserId, installation, ct)
            && await _repository.CountDevicesAsync(command.UserId, ct) >= document.ListenerHealth.MaxDevicesPerUser)
        {
            throw new BusinessRuleException(
                ListenerHealthErrorCodes.TooManyDevices, $"Đã đạt số thiết bị tối đa ({document.ListenerHealth.MaxDevicesPerUser}) cho một tài khoản.");
        }

        var now = _now();
        var known = await _repository.GetKnownPackagesAsync(command.UserId, ct);
        var packages = (command.SelectedPackages ?? [])
            .Where(p => p.PackageName is { } name && known.Contains(name.Trim()))
            .GroupBy(p => p.PackageName!.Trim(), StringComparer.Ordinal)
            .Select(g => new StoredPackage(
                g.Key,
                g.Select(p => p.NotificationsEnabled).LastOrDefault(v => v is not null),
                g.Max(p => p.LastPostedAt) is { } posted ? (posted > now + ClockSkew ? now : posted) : null))
            .ToList();

        await _repository.RecordReportAsync(new ListenerReport(
            command.UserId,
            installation,
            command.PermissionGranted!.Value,
            command.ListenerConnected!.Value,
            JsonSerializer.Serialize(packages, Json),
            Clean(command.AppVersion),
            Clean(command.Manufacturer)?.ToLowerInvariant(),
            now,
            VietnamTime.DateOf(now)), ct);

        await _cache.RemoveAsync(DataCoverageCache.Key(command.UserId, DataCoverageCache.ForecastWindowDays(document.Forecast)), ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Một mục <c>selected_packages</c> như đã lưu (camelCase trong JSON).</summary>
public sealed record StoredPackage(string PackageName, bool? NotificationsEnabled, DateTimeOffset? LastPostedAt);
