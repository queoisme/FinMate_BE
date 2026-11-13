namespace FinMate.Domain.Enums;

/// <summary>Loại thao tác đã ghi ảnh chụp trong <c>transaction_merge_snapshots</c> (Phase 83, phần B).</summary>
public enum MergeSnapshotKind
{
    /// <summary>Gộp hai bản trùng (Phase 82).</summary>
    DuplicateMerge,

    /// <summary>Ghép hai chiều chi/thu thành một Transfer.</summary>
    TransferPair,

    /// <summary>Đổi một chiều thành Transfer tới ví người dùng chọn.</summary>
    TransferConvert,
}
