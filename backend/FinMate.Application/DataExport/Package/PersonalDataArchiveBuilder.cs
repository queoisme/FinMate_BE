using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.DataExport.Package;

public sealed record ArchiveBuildResult(long SizeBytes, IReadOnlyList<string> Files, bool AgentMemoryUnavailable);

/// <summary>
/// Dựng ZIP gói dữ liệu cá nhân (Phase 91, BR-EXP-07/08): README.txt, một JSON mỗi nhóm dữ liệu, CSV giao dịch (cùng định dạng BR-EXP-02, luôn dấu phẩy).
/// Ghi qua một luồng đếm byte và dừng ngay khi vượt <paramref name="maxBytes"/> (<see cref="ArchiveTooLargeException"/>). Không log nội dung hay số tiền.
/// </summary>
public sealed class PersonalDataArchiveBuilder(
    IPersonalDataReader reader,
    IDataExportRepository transactions,
    IAgentAiClient agent,
    ILogger<PersonalDataArchiveBuilder> logger)
{
    private const int AgentAttempts = 3;
    private static readonly IReadOnlyCollection<TransactionStatus> AllStatuses = [TransactionStatus.Confirmed, TransactionStatus.Draft];

    /// <summary>Khoảng nghỉ giữa các lần hỏi lại ai-service (D2); test đặt 0.</summary>
    public TimeSpan AgentRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<ArchiveBuildResult> BuildAsync(Guid userId, Stream output, long maxBytes, DateTimeOffset exportedAt, CancellationToken ct = default)
    {
        var limited = new LimitedWriteStream(output, maxBytes);
        var files = new List<string>();
        var sections = await reader.ReadSectionsAsync(userId, ct);
        var (memory, memoryUnavailable) = await AgentMemoryAsync(userId, ct);

        using (var zip = new ZipArchive(limited, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var section in sections)
            {
                await WriteJsonAsync(zip, section, exportedAt, ct);
                files.Add(section.FileName);
            }

            await WriteJsonAsync(zip, new PackageSection("agent_memory.json", memoryUnavailable ? new { unavailable = true } : memory, !memoryUnavailable), exportedAt, ct);
            files.Add("agent_memory.json");

            await WriteTransactionsAsync(zip, "transactions.csv", userId, deleted: false, ct);
            await WriteTransactionsAsync(zip, "transactions_deleted.csv", userId, deleted: true, ct);
            files.AddRange(["transactions.csv", "transactions_deleted.csv"]);
            files.Sort(StringComparer.Ordinal);

            await using var readme = zip.CreateEntry("README.txt", CompressionLevel.Optimal).Open();
            await readme.WriteAsync(Encoding.UTF8.GetPreamble(), ct);
            await readme.WriteAsync(Encoding.UTF8.GetBytes(PackageReadme.Build(exportedAt, files, memoryUnavailable)), ct);
        }

        await limited.FlushAsync(ct);
        return new ArchiveBuildResult(limited.Written, ["README.txt", .. files], memoryUnavailable);
    }

    private static async Task WriteJsonAsync(ZipArchive zip, PackageSection section, DateTimeOffset exportedAt, CancellationToken ct)
    {
        object envelope = section.IsList
            ? new { formatVersion = TransactionCsvFormat.FormatVersion, exportedAt, items = section.Data }
            : new { formatVersion = TransactionCsvFormat.FormatVersion, exportedAt, data = section.Data };
        await using var entry = zip.CreateEntry(section.FileName, CompressionLevel.Optimal).Open();
        await JsonSerializer.SerializeAsync(entry, envelope, PackageJson.Options, ct);
    }

    private async Task WriteTransactionsAsync(ZipArchive zip, string name, Guid userId, bool deleted, CancellationToken ct)
    {
        await using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        await using var csv = new CsvWriter(entry, CsvDelimiter.Comma);
        await csv.WriteRowAsync(TransactionCsvFormat.HeaderCells(deleted), ct);
        var filter = new TransactionExportFilter(null, null, null, null, AllStatuses, DeletedOnly: deleted);
        await foreach (var row in transactions.StreamTransactionsAsync(userId, filter, ct))
        {
            await csv.WriteRowAsync(TransactionCsvFormat.ToCells(row, deleted), ct);
        }
    }

    /// <summary>D2: bộ nhớ Agent nằm ở ai-service. Không trả lời sau vài lần thử thì gói vẫn xong, file đánh dấu <c>unavailable</c>.</summary>
    private async Task<(object Items, bool Unavailable)> AgentMemoryAsync(Guid userId, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= AgentAttempts; attempt++)
        {
            try
            {
                var entries = await agent.GetMemoryAsync(userId, ct);
                return (entries.Select(e => new { key = e.Key, value = e.Value, updatedAt = e.UpdatedAt }).ToList(), false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Không đọc được bộ nhớ Agent cho gói dữ liệu (lần {Attempt}): {Error}", attempt, ex.GetType().Name);
                if (attempt < AgentAttempts && AgentRetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(AgentRetryDelay * attempt, ct);
                }
            }
        }

        return (Array.Empty<object>(), true);
    }
}
