namespace FinMate.Domain.Enums;

/// <summary>Loại chuyển tiền giữa các ví của chính người dùng mà AI nhận ra từ thông báo (Phase 83). Cột lưu dạng snake_case.</summary>
public enum TransferKind
{
    /// <summary>Rút tiền mặt tại ATM: tiền ra khỏi ngân hàng, vào ví Tiền mặt.</summary>
    AtmWithdrawal,

    /// <summary>Nộp tiền mặt tại ATM/CDM: tiền từ ví Tiền mặt vào ngân hàng.</summary>
    AtmDeposit,

    /// <summary>Nạp ví điện tử từ ngân hàng.</summary>
    WalletTopup,

    /// <summary>Rút từ ví điện tử về ngân hàng.</summary>
    WalletWithdrawal,

    /// <summary>Chuyển giữa hai tài khoản chính chủ.</summary>
    OwnAccount,
}

public static class TransferKindColumn
{
    public static string ToColumn(TransferKind kind) => kind switch
    {
        TransferKind.AtmWithdrawal => "atm_withdrawal",
        TransferKind.AtmDeposit => "atm_deposit",
        TransferKind.WalletTopup => "wallet_topup",
        TransferKind.WalletWithdrawal => "wallet_withdrawal",
        _ => "own_account",
    };

    /// <summary>Giá trị lạ (hợp đồng AI mới hơn backend) → null, không ném.</summary>
    public static TransferKind? Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "atm_withdrawal" => TransferKind.AtmWithdrawal,
        "atm_deposit" => TransferKind.AtmDeposit,
        "wallet_topup" => TransferKind.WalletTopup,
        "wallet_withdrawal" => TransferKind.WalletWithdrawal,
        "own_account" => TransferKind.OwnAccount,
        _ => null,
    };

    public static TransferKind FromColumn(string value) => Parse(value) ?? throw new ArgumentOutOfRangeException(nameof(value), value, null);
}
