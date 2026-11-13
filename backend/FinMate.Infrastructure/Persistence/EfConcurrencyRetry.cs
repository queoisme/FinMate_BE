using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMate.Infrastructure.Persistence;

public sealed class EfConcurrencyRetry : IConcurrencyRetry
{
    private const int MaxRetries = 3;
    private const int MaxBackoffMilliseconds = 60;
    private const string UniqueViolation = "23505";
    private const string DeadlockDetected = "40P01";
    private const string BudgetPeriodUniqueIndex = "uq_budget_periods_budget_start";

    private readonly FinMateDbContext _context;

    public EfConcurrencyRetry(FinMateDbContext context)
    {
        _context = context;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(ct);
            }
            catch (Exception ex) when (IsLostMoneyRace(ex) || IsDeadlock(ex))
            {
                if (attempt >= MaxRetries)
                {
                    throw new ConflictException(ConcurrencyErrorCodes.ConcurrentUpdate, ConcurrencyErrorCodes.ConcurrentUpdateMessage);
                }

                // Bỏ toàn bộ số đã đọc: lượt sau phải tính lại trên dữ liệu mới nhất.
                _context.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(1, MaxBackoffMilliseconds) * (attempt + 1), ct);
            }
        }
    }

    /// <summary>
    /// Chỉ nhận đúng xung đột trên dòng tiền, vốn xảy ra ở lần lưu tiền. Xung đột khác (vd gỡ token thiết bị sau khi đã lưu) có thể
    /// đến khi tiền đã được ghi, chạy lại sẽ tính hai lần.
    /// </summary>
    private static bool IsLostMoneyRace(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException conflict => conflict.Entries.Any(e => e.Entity is FinancialAccount or BudgetPeriod or SavingGoal),
        DbUpdateException update => update.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: BudgetPeriodUniqueIndex },
        _ => false,
    };

    /// <summary>
    /// Phase 82: hai thao tác khoá các dòng giao dịch theo thứ tự khác nhau (gộp khoá theo id tăng dần; xoá một khoản hoàn khoá chính nó rồi tới
    /// khoản gốc) thì Postgres huỷ một bên với 40P01. Bên bị huỷ chưa ghi gì (cả DB transaction đã rollback), nên chạy lại toàn bộ là an toàn.
    /// </summary>
    private static bool IsDeadlock(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}
