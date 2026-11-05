namespace FinMate.Application.Common.Interfaces;

/// <summary>
/// Chạy lại một thao tác ghi tiền khi nó thua một request đồng thời. Chỉ bọc handler không có tác dụng phụ bên ngoài TRƯỚC lần lưu tiền
/// (đọc, tính rồi một lần <c>SaveChanges</c>): lượt thất bại chưa lưu gì nên chạy lại không thể tính tiền hai lần.
/// Hết lượt thì ném <see cref="Exceptions.ConflictException"/> với mã <see cref="Exceptions.ConcurrencyErrorCodes.ConcurrentUpdate"/>.
/// </summary>
public interface IConcurrencyRetry
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}
