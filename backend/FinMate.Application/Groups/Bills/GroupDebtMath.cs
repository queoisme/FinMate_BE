using FinMate.Domain.Entities;

namespace FinMate.Application.Groups.Bills;

public sealed record DebtTransfer(Guid FromUserId, Guid ToUserId, long AmountCents);

/// <summary>Toán nợ thuần, toàn số nguyên (đồng). Không đọc DB để test được riêng.</summary>
public static class GroupDebtMath
{
    /// <summary>Khóa gộp mọi người đã xóa cứng tài khoản.</summary>
    public static readonly Guid Former = Guid.Empty;

    /// <summary>
    /// Chia đều <paramref name="amountCents"/> cho <paramref name="participants"/>: mỗi người phần nguyên, phần dư dồn hết cho người trả
    /// (nếu người trả có tham gia), không thì cho người đầu danh sách. Tổng luôn bằng số tiền.
    /// </summary>
    public static IReadOnlyList<(Guid UserId, long ShareCents)> EqualShares(long amountCents, IReadOnlyList<Guid> participants, Guid paidBy)
    {
        var each = amountCents / participants.Count;
        var remainder = amountCents - (each * participants.Count);
        var remainderTo = participants.Contains(paidBy) ? paidBy : participants[0];
        return participants.Select(p => (p, each + (p == remainderTo ? remainder : 0))).ToList();
    }

    /// <summary>
    /// Số dư ròng mỗi người: dương = đang được nợ, âm = đang nợ. Người trả được cộng cả hóa đơn rồi trừ phần của mình; người trả nợ (from) được cộng,
    /// người nhận (to) bị trừ. <see cref="Former"/> là người đã xóa cứng tài khoản (id null trong DB), gộp lại một dòng.
    /// </summary>
    public static Dictionary<Guid, long> Net(IEnumerable<GroupBill> bills, IEnumerable<GroupSettlement> settlements)
    {
        var net = new Dictionary<Guid, long>();
        void Add(Guid? user, long delta) => net[user ?? Former] = net.GetValueOrDefault(user ?? Former) + delta;

        foreach (var bill in bills)
        {
            Add(bill.PaidByUserId, bill.AmountCents);
            foreach (var split in bill.Splits)
            {
                Add(split.UserId, -split.ShareCents);
            }
        }

        foreach (var s in settlements)
        {
            Add(s.FromUserId, s.AmountCents);
            Add(s.ToUserId, -s.AmountCents);
        }

        return net;
    }

    /// <summary>
    /// Ít lần chuyển nhất theo cách tham lam: người nợ nhiều nhất trả cho người được nợ nhiều nhất, lặp lại. Tối đa (số người − 1) lần.
    /// Bỏ qua người đã xóa cứng (không có ai để chuyển). Thứ tự cố định theo (số tiền giảm, id tăng) nên kết quả không đổi giữa các lần gọi.
    /// </summary>
    public static IReadOnlyList<DebtTransfer> MinimalTransfers(IReadOnlyDictionary<Guid, long> net)
    {
        var creditors = net.Where(p => p.Key != Former && p.Value > 0).Select(p => (Id: p.Key, Left: p.Value)).OrderByDescending(p => p.Left).ThenBy(p => p.Id).ToList();
        var debtors = net.Where(p => p.Key != Former && p.Value < 0).Select(p => (Id: p.Key, Left: -p.Value)).OrderByDescending(p => p.Left).ThenBy(p => p.Id).ToList();
        var result = new List<DebtTransfer>();
        int c = 0, d = 0;
        while (c < creditors.Count && d < debtors.Count)
        {
            var pay = Math.Min(creditors[c].Left, debtors[d].Left);
            result.Add(new DebtTransfer(debtors[d].Id, creditors[c].Id, pay));
            creditors[c] = (creditors[c].Id, creditors[c].Left - pay);
            debtors[d] = (debtors[d].Id, debtors[d].Left - pay);
            if (creditors[c].Left == 0)
            {
                c++;
            }

            if (debtors[d].Left == 0)
            {
                d++;
            }
        }

        return result;
    }
}
