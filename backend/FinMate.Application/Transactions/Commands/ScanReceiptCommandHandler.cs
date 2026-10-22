using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Ảnh hóa đơn → các trường để client điền sẵn form (docx phương thức 3).
///
/// Không persist gì, đúng như <see cref="ParseNaturalLanguageCommandHandler"/>: docx yêu cầu
/// người dùng rà soát rồi mới lưu, nên giao dịch chỉ ra đời khi họ bấm Lưu và đi qua
/// <c>POST /transactions</c> với <c>source = Receipt</c>.
///
/// Ảnh cũng không được lưu ở đâu: nó chỉ đi qua bộ nhớ tiến trình rồi sang AI Service / OCR service.
///
/// Có <c>OCR_SERVICE_URL</c> thì backend điều phối: OCR service đọc và lọc (ra tổng tiền, tên, ngày, chữ đã lọc), rồi AI Service
/// phân loại chữ đã lọc đó (<c>POST /api/v1/bill/classify</c>). Không cấu hình thì quay về <c>POST /api/v1/ocr</c> như trước.
/// </summary>
public class ScanReceiptCommandHandler : IScanReceiptCommandHandler
{
    // Khớp giới hạn phía AI Service. Chặn ở cả hai đầu: chặn sớm ở đây để một ảnh 50MB
    // không phải đi hết một vòng mạng nội bộ mới bị từ chối.
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif",
    };

    private const string SuccessResult = "success";
    private const string NoAmountResult = "no_amount";
    private const string UnreadableResult = "unreadable";
    private const string DebitType = "debit";
    private const string LabelledTotalSource = "text";

    private readonly IAIServiceClient _aiServiceClient;
    private const int MaxMerchantLength = 200;

    private const int MaxItemNameLength = 120;
    private const int MaxSourceLength = 16;
    private const string UnknownSource = "unknown";
    // Nhóm chung của các món khi bill không tách được (thường vì mọi món cùng một nhóm). Khác "items" (ReceiptScan.ItemsCategorySource): nguồn đó nghĩa là bill đã tách.
    private const string ItemVoteSource = "item_vote";

    /// <summary>Tên món bù khi OCR đọc thiếu món so với tổng bill; không bao giờ gửi cho AI hay lưu như một món thật.</summary>
    private const string UnreadItemsName = "Các món chưa đọc được";
    private const string MemorySource = "memory";
    private const int ItemVoteSharePercent = 60;
    private const string DiscountSource = "discount";
    private const string SurchargeSource = "surcharge";

    // 9 nhóm của bộ luật bill (OCR service) -> slug hệ thống của backend. Cùng bảng với ai-service/app/core/category_map.py.
    private static readonly Dictionary<string, string> LocalToSlug = new()
    {
        ["dining"] = "food", ["groceries"] = "food", ["shopping"] = "shopping", ["transport"] = "transport", ["housing"] = "housing",
        ["utilities"] = "bills", ["healthcare"] = "health", ["entertainment"] = "entertainment", ["other"] = "other",
    };

    // Phần tiền chưa rõ nhóm từ mức này của tổng bill trở lên thì app nên bảo người dùng xem lại.
    private const int UnknownSharePercentForReview = 20;

    // Hóa đơn thường được quét sau lúc chi một lúc (khác chat), nên cửa sổ rộng hơn chat. Chỉ cảnh báo.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(3);

    private readonly IOcrServiceClient _ocrServiceClient;
    private readonly IReceiptScanRepository _receiptScanRepository;
    private readonly ReceiptOptions _options;
    private readonly IUserRepository _userRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly ILogger<ScanReceiptCommandHandler> _logger;
    private readonly ITrainingSampleService? _trainingSamples;  // null: không có dịch vụ thu mẫu (test cũ) thì bỏ qua

    public ScanReceiptCommandHandler(
        IAIServiceClient aiServiceClient,
        IOcrServiceClient ocrServiceClient,
        IReceiptScanRepository receiptScanRepository,
        ReceiptOptions options,
        IUserRepository userRepository,
        ITransactionRepository transactionRepository,
        ILogger<ScanReceiptCommandHandler> logger,
        ITrainingSampleService? trainingSamples = null)
    {
        _aiServiceClient = aiServiceClient;
        _ocrServiceClient = ocrServiceClient;
        _receiptScanRepository = receiptScanRepository;
        _options = options;
        _userRepository = userRepository;
        _transactionRepository = transactionRepository;
        _logger = logger;
        _trainingSamples = trainingSamples;
    }

    public async Task<ScannedReceiptDto> HandleAsync(
        ScanReceiptCommand command, CancellationToken ct = default)
    {
        if (command.Image.Length == 0)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageInvalid, "Ảnh rỗng.");
        }

        if (command.Image.Length > MaxImageBytes)
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageTooLarge,
                $"Ảnh tối đa {MaxImageBytes / (1024 * 1024)}MB.");
        }

        if (!AllowedContentTypes.Contains(command.ContentType))
        {
            throw new BusinessRuleException(
                TransactionErrorCodes.ReceiptImageInvalid,
                "Chỉ nhận ảnh JPEG, PNG, WebP hoặc HEIC.");
        }

        if (_ocrServiceClient.IsConfigured)
        {
            return await ScanViaOcrServiceAsync(command, ct);
        }

        var response = await _aiServiceClient.ScanReceiptAsync(
            new ScanReceiptRequest(command.UserId, command.Image, command.FileName, command.ContentType),
            ct);

        var dto = new ScannedReceiptDto(
            response.OcrResult,
            response.Extraction?.AmountCents,
            response.Extraction?.TransactionType,
            response.Extraction?.MerchantName,
            response.Extraction?.TransactedAt,
            response.Categorization?.CategorySlug,
            response.Extraction?.Confidence);
        return await RecordAsync(command.UserId, dto, categorySource: null, needsReview: false, ct);
    }

    private async Task<ScannedReceiptDto> ScanViaOcrServiceAsync(ScanReceiptCommand command, CancellationToken ct)
    {
        var ocr = await _ocrServiceClient.AnalyzeAsync(
            new OcrServiceRequest(command.UserId, command.Image, command.FileName, command.ContentType), ct);

        if (!ocr.Readable)
        {
            return await RecordAsync(command.UserId, Empty(UnreadableResult), null, false, ct);
        }

        if (ocr.TotalAmount is not > 0)
        {
            return await RecordAsync(command.UserId, Empty(NoAmountResult), null, ocr.NeedsReview, ct);
        }

        var merchant = ocr.MerchantDisplay ?? ocr.MerchantName;
        var confidence = ReceiptConfidence.Compute(
            hasAmount: true,
            amountFromLabel: ocr.TotalSource == LabelledTotalSource,
            hasMerchant: !string.IsNullOrWhiteSpace(merchant),
            hasDate: ocr.TransactedAt is not null,
            needsReview: ocr.NeedsReview);

        if (ocr.Orders is { Count: > 1 } orders)
        {
            return await ScanOrderListAsync(command.UserId, ocr, orders, merchant, confidence, ct);
        }

        var (split, itemVote) = await TrySplitAsync(command.UserId, ocr, merchant, confidence, ct);
        if (split is not null)
        {
            return split;
        }

        var category = await ClassifyAsync(command.UserId, ocr, ct);
        if (itemVote is not null && category?.Source != MemorySource)
        {
            // Đường cũ chỉ nhìn cửa hàng và chữ (không có AI) nên hay không ra nhóm hoặc theo thương hiệu ("Kids Plaza" = mua sắm cho cả hóa đơn sữa).
            // Các món đã được phân loại cùng một nhóm là bằng chứng tốt hơn, trừ khi người dùng đã chốt nhóm cho cửa hàng này.
            category = new BillClassifyResponse(itemVote, null, 0.8, ItemVoteSource);
        }

        var dto = new ScannedReceiptDto(
            SuccessResult, ocr.TotalAmount, DebitType, merchant, ocr.TransactedAt, category?.CategorySlug, confidence,
            PossibleDuplicateTransactionId: await DuplicateOfAsync(command.UserId, ocr.TotalAmount.Value, ocr.TransactedAt, ct));
        // Cờ tách nhóm bật mà các dòng món không khớp tổng bill (ItemsOk = false): bill phải về một nhóm, người dùng nên xem lại.
        var needsReview = ocr.NeedsReview || (_options.SplitByItemsEnabled && !ocr.ItemsOk);
        var recorded = await RecordAsync(command.UserId, dto, category?.Source, needsReview, ct);
        // Cùng tên cửa hàng mà bộ phân loại đã thấy (ClassifyAsync dùng tên OCR), không phải tên hiển thị đã làm đẹp.
        await RecordTrainingSampleAsync(command.UserId, recorded, ocr.MerchantName ?? ocr.MerchantDisplay, ocr, category, ct);
        return recorded;
    }

    /// <summary>
    /// Mẫu chờ nhãn cho vòng tự học: chỉ cho bill MỘT nhóm (bill tách nhóm và danh sách đơn đã thoát ra từ trước), chỉ chữ ĐÃ lọc của OCR service,
    /// và dịch vụ chỉ gửi khi người dùng đang đồng ý. Không bao giờ ném lỗi.
    /// </summary>
    private async Task RecordTrainingSampleAsync(
        Guid userId, ScannedReceiptDto scanned, string? merchant, OcrServiceResponse ocr, BillClassifyResponse? category, CancellationToken ct)
    {
        if (_trainingSamples is null || scanned.ScanId is not { } scanId)
        {
            return;
        }

        await _trainingSamples.RecordAsync(userId, scanId, merchant, ocr.Lines, ocr.TotalAmount, category?.CategorySlug, category?.Source, ct);
    }

    /// <summary>
    /// Ảnh chụp màn hình danh sách đơn (nhiều đơn): mỗi đơn là một khoản riêng, phân loại theo cửa hàng + sản phẩm của chính nó.
    /// Một lần quét chỉ ghi MỘT bản ghi (khoản đầu); app tự nối từng đơn sang giao dịch khi người dùng bấm lưu.
    /// </summary>
    private async Task<ScannedReceiptDto> ScanOrderListAsync(
        Guid userId, OcrServiceResponse ocr, IReadOnlyList<OcrOrder> orders, string? merchant, double confidence, CancellationToken ct)
    {
        var scanned = new List<ScannedOrder>();
        foreach (var order in orders)
        {
            var name = order.MerchantDisplay ?? order.MerchantName;
            var category = await ClassifyOrderAsync(userId, order, ct);
            // AI Service không nhận ra (hoặc lỗi) thì giữ nhóm OCR service đã đoán: màn "Đơn đã mua" của sàn thương mại luôn là mua sắm.
            var slug = category?.CategorySlug ?? (order.Category is { } local && LocalToSlug.TryGetValue(local, out var mapped) ? mapped : null);
            scanned.Add(new ScannedOrder(name, order.Amount, order.Product, slug));
        }

        var dto = new ScannedReceiptDto(
            SuccessResult, ocr.TotalAmount, DebitType, merchant, ocr.TransactedAt, scanned[0].CategorySlug, confidence,
            Orders: scanned, IncompleteOrders: ocr.IncompleteOrders);
        return await RecordAsync(userId, dto, categorySource: null, ocr.NeedsReview, ct);
    }

    private async Task<BillClassifyResponse?> ClassifyOrderAsync(Guid userId, OcrOrder order, CancellationToken ct)
    {
        try
        {
            return await _aiServiceClient.ClassifyBillAsync(
                new BillClassifyRequest(userId, order.MerchantName ?? order.MerchantDisplay, order.Product is null ? [] : [order.Product]), ct);
        }
        catch (AIServiceUnavailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ghi lại kết quả BÓC TÁCH của lần quét (không ảnh, không chữ OCR) và trả về chính nó kèm mã bản ghi. Bản ghi không động tới số dư,
    /// ngân sách hay gamification: lần quét chưa phải giao dịch, giao dịch chỉ ra đời khi người dùng bấm Lưu.
    /// </summary>
    private async Task<ScannedReceiptDto> RecordAsync(
        Guid userId, ScannedReceiptDto dto, string? categorySource, bool needsReview, CancellationToken ct)
    {
        var scan = await RecordScanAsync(userId, dto, categorySource, needsReview, ct);
        return dto with { ScanId = scan.Id, NeedsReview = needsReview };
    }

    private async Task<ReceiptScan> RecordScanAsync(
        Guid userId, ScannedReceiptDto dto, string? categorySource, bool needsReview, CancellationToken ct)
    {
        var scan = new ReceiptScan
        {
            UserId = userId,
            OcrResult = dto.OcrResult,
            AmountCents = dto.AmountCents,
            MerchantName = dto.MerchantName is { Length: > MaxMerchantLength } name ? name[..MaxMerchantLength] : dto.MerchantName,
            TransactedAt = dto.TransactedAt,
            CategorySlug = dto.CategorySlug,
            CategorySource = categorySource,
            Confidence = dto.Confidence,
            NeedsReview = needsReview,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _receiptScanRepository.AddAsync(scan, ct);
        return scan;
    }

    /// <summary>
    /// Tách bill theo món. Trả null (bên gọi đi đường cũ một nhóm) khi cờ tắt, OCR không chắc các dòng, dưới 2 món thường,
    /// ai-service lỗi hay trả sai, hoặc <see cref="ReceiptSplitter"/> không ra được các nhóm khớp tổng bill.
    /// AI chỉ nhận TÊN món thường (không tiền, không giảm giá/phụ thu); log không bao giờ chứa tên hay tiền.
    /// </summary>
    private async Task<(ScannedReceiptDto? Split, string? ItemVote)> TrySplitAsync(
        Guid userId, OcrServiceResponse ocr, string? merchant, double confidence, CancellationToken ct)
    {
        // Không đòi ItemsOk ở đây: phân loại TÊN món không cần giá, nên một dòng OCR đọc sót không được làm cả bill mất phân loại.
        // ItemsOk chỉ còn quyết định có CHIA TIỀN thành nhóm hay không (xem bên dưới).
        if (!_options.SplitByItemsEnabled || ocr.Items is not { } items)
        {
            return NoSplit;
        }

        // Chỉ số dòng gốc của các món thường; vị trí trong danh sách này là chỉ số gửi sang ai-service.
        var regular = Enumerable.Range(0, items.Count)
            .Where(i => !items[i].IsDiscount && !ReceiptSplitter.IsSurcharge(items[i]))
            .ToList();
        var regularIndexOf = regular.Select((original, k) => (original, k)).ToDictionary(p => p.original, p => p.k);

        // Topping theo món gốc nên cần ít nhất 2 món gốc mới có thể ra 2 nhóm; topping mồ côi (cha là giảm giá/phụ thu)
        // thì Split chắc chắn trả null. Chặn trước khi đọc DB và gọi AI: không gửi tên món đi khi không thể tách.
        var roots = regular.Count(i => items[i].Parent is null);
        if (roots < 2 || regular.Any(i => items[i].Parent is { } p && !regularIndexOf.ContainsKey(p)))
        {
            return NoSplit;
        }

        // Đọc thẳng từ DB mỗi lần quét: người dùng vừa rút đồng ý thì lần quét kế tiếp đã không được gửi Gemini.
        var allowAi = (await _userRepository.GetByIdAsync(userId, ct))?.AiConsentGranted ?? false;

        // Cùng giá trị như /bill/classify: tên OCR thô để nhận thương hiệu (tên hiển thị đã sửa có thể làm hỏng nhóm mặc định).
        var request = new ClassifyItemsRequest(
            userId,
            ocr.MerchantName ?? ocr.MerchantDisplay,
            regular.Select(i => new ClassifyItemInput(
                items[i].Name,
                items[i].Parent is { } parent && regularIndexOf.TryGetValue(parent, out var k) ? k : null)).ToList(),
            allowAi);

        ClassifyItemsResponse? response;
        try
        {
            response = await _aiServiceClient.ClassifyItemsAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Client thật có thể ném cả NullReferenceException khi phản hồi lệch schema, không chỉ AIServiceUnavailableException.
            _logger.LogWarning("Tách nhóm hóa đơn bỏ qua: classify-items lỗi {ExceptionType}", ex.GetType().Name);
            return NoSplit;
        }

        if (response?.Items is not { } replies || replies.Count != regular.Count)
        {
            return NoSplit;
        }

        // Trải lại thành danh sách cùng độ dài, cùng thứ tự với ocr.Items; giảm giá/phụ thu giữ chỗ (Split bỏ qua các vị trí này).
        var expanded = Enumerable.Range(0, items.Count).Select(i => new ClassifiedItem(i, null, UnknownSource)).ToArray();
        var filled = new bool[regular.Count];
        foreach (var reply in replies)
        {
            if (reply is null || reply.Index < 0 || reply.Index >= regular.Count || filled[reply.Index])
            {
                return NoSplit;
            }

            filled[reply.Index] = true;
            var original = regular[reply.Index];
            expanded[original] = new ClassifiedItem(original, reply.CategorySlug, reply.Source ?? UnknownSource);
        }

        // Các dòng chưa cộng đúng tổng (ItemsOk = false): chỉ chia khi các món đọc được CÒN THIẾU so với tổng, bằng cách thêm một món "chưa đọc được"
        // (nhóm chưa rõ) cho đủ tổng, và luôn báo xem lại. Đọc thừa hoặc đủ mà vẫn không được xác nhận thì không chia: số tiền chưa kiểm chứng được.
        var splitItems = items;
        var splitClassified = expanded;
        var shortfall = ocr.TotalAmount!.Value - items.Sum(i => i.Amount);
        if (!ocr.ItemsOk && shortfall > 0)
        {
            splitItems = [.. items, new OcrItem(UnreadItemsName, shortfall, null, false)];
            splitClassified = [.. expanded, new ClassifiedItem(items.Count, null, UnknownSource)];
        }

        var groups = ocr.ItemsOk || shortfall > 0 ? ReceiptSplitter.Split(splitItems, splitClassified, ocr.TotalAmount!.Value) : null;
        if (groups is null)
        {
            return (null, ItemVote(items, expanded));
        }

        var flagged = new List<ReceiptGroup>();
        foreach (var group in groups)
        {
            flagged.Add(group with { PossibleDuplicateTransactionId = await DuplicateOfAsync(userId, group.AmountCents, ocr.TransactedAt, ct) });
        }

        groups = flagged;

        // App cũ chỉ đọc slug trên cùng: lấy nhóm lớn nhất CÓ danh mục (Split bảo đảm có ít nhất hai nhóm như vậy).
        var dto = new ScannedReceiptDto(
            SuccessResult, ocr.TotalAmount, DebitType, merchant, ocr.TransactedAt,
            groups.First(g => g.CategorySlug is not null).CategorySlug, confidence,
            Groups: groups);
        var unknownCents = groups.Where(g => g.CategorySlug is null).Sum(g => g.AmountCents);
        var needsReview = ocr.NeedsReview || !ocr.ItemsOk || unknownCents * 100 >= ocr.TotalAmount!.Value * UnknownSharePercentForReview;
        var scan = await RecordScanAsync(userId, dto, ReceiptScan.ItemsCategorySource, needsReview, ct);

        // scan vừa tạo trong chính request này, nên AddItemsAsync (không tự kiểm chủ sở hữu) an toàn.
        try
        {
            await _receiptScanRepository.AddItemsAsync(
                scan.Id, items.Select((item, i) => ToScanItem(item, i, expanded[i])).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Lần quét đã ghi; mất các dòng món chỉ mất phần chi tiết, không đáng làm hỏng kết quả người dùng đang chờ.
            _logger.LogWarning("Không lưu được các món của lần quét: {ExceptionType}", ex.GetType().Name);
        }

        return (dto with { ScanId = scan.Id, NeedsReview = needsReview }, null);
    }

    private static readonly (ScannedReceiptDto? Split, string? ItemVote) NoSplit = (null, null);

    /// <summary>
    /// Bill không tách thành nhiều nhóm, nhưng các món ĐÃ BIẾT nhóm cùng một slug và chiếm đa số số tiền các món thường thì đó là nhóm của cả bill.
    /// Trả slug khi chắc, null thì bên gọi giữ đường cũ.
    /// </summary>
    private static string? ItemVote(IReadOnlyList<OcrItem> items, ClassifiedItem[] classified)
    {
        var regular = Enumerable.Range(0, items.Count)
            .Where(i => !items[i].IsDiscount && !ReceiptSplitter.IsSurcharge(items[i]))
            .ToList();
        var known = regular.Where(i => classified[i].Source != UnknownSource && classified[i].CategorySlug is not null).ToList();
        var slugs = known.Select(i => classified[i].CategorySlug).Distinct().ToList();
        if (slugs.Count != 1)
        {
            return null;
        }

        var regularCents = regular.Sum(i => items[i].Amount);
        var knownCents = known.Sum(i => items[i].Amount);
        return regularCents > 0 && knownCents * 100 >= regularCents * ItemVoteSharePercent ? slugs[0] : null;
    }

    private Task<Guid?> DuplicateOfAsync(Guid userId, long amountCents, DateTimeOffset? at, CancellationToken ct)
        => _transactionRepository.FindPossibleDuplicateAsync(
            userId, amountCents, at ?? DateTimeOffset.UtcNow, DuplicateWindow, null, ct);

    private static ReceiptScanItem ToScanItem(OcrItem item, int position, ClassifiedItem classified)
    {
        // Cùng quy tắc với ReceiptSplitter: nguồn "unknown" nghĩa là chưa rõ nhóm dù AI có gửi slug.
        var (slug, source) = item.IsDiscount ? (null, DiscountSource)
            : ReceiptSplitter.IsSurcharge(item) ? (null, SurchargeSource)
            : (classified.Source == UnknownSource ? null : classified.CategorySlug, classified.Source);

        return new ReceiptScanItem
        {
            Position = position,
            Name = item.Name.Length > MaxItemNameLength ? item.Name[..MaxItemNameLength] : item.Name,
            AmountCents = item.Amount,
            CategorySlug = slug,
            CategorySource = source.Length > MaxSourceLength ? source[..MaxSourceLength] : source,
            ParentPosition = item.Parent,
        };
    }

    /// <summary>
    /// Phân loại là phần phụ: AI Service lỗi thì vẫn trả tổng tiền và tên đọc được, danh mục để trống cho người dùng chọn.
    /// Chặn cả lần quét chỉ vì không phân loại được là bắt người dùng chụp lại một ảnh đã đọc xong.
    /// </summary>
    private async Task<BillClassifyResponse?> ClassifyAsync(Guid userId, OcrServiceResponse ocr, CancellationToken ct)
    {
        // Cửa hàng người dùng đã chốt do AI Service tự tra theo hash người dùng; backend chỉ gửi tên và chữ đã lọc.
        try
        {
            return await _aiServiceClient.ClassifyBillAsync(
                new BillClassifyRequest(userId, ocr.MerchantName ?? ocr.MerchantDisplay, ocr.Lines), ct);
        }
        catch (AIServiceUnavailableException)
        {
            return null;
        }
    }

    private static ScannedReceiptDto Empty(string result)
        => new(result, null, null, null, null, null, null);
}
