namespace FinMate.Application.Common.Models;

/// <summary>Một trang của danh sách phân trang kiểu offset: các dòng của trang và tổng số dòng khớp bộ lọc (để web vẽ "x–y / tổng").</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit)
{
    /// <summary>Đặt tổng và vị trí trang vào <c>meta</c> để <c>data</c> vẫn là mảng như trước (client cũ không vỡ).</summary>
    public ApiMeta ToMeta() => new(Total: Total, Offset: Offset, Limit: Limit);

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new(Items.Select(map).ToList(), Total, Offset, Limit);
}
