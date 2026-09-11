namespace FinMate.Application.Gamification;

/// <summary>
/// Đường cong level bậc 2: tổng EXP để đạt level N là <c>50·N·(N-1)</c> —
/// lvl2 = 100, lvl3 = 300, lvl4 = 600, lvl5 = 1000. Mỗi level sau cần nhiều hơn level trước,
/// nên cảm giác tiến bộ không phẳng như mốc cố định.
/// </summary>
public static class LevelCurve
{
    /// <summary>Chặn trên để một giá trị EXP hỏng không làm vòng lặp chạy mãi.</summary>
    public const int MaxLevel = 999;

    public static int TotalExpForLevel(int level) => 50 * level * (level - 1);

    /// <summary>
    /// Level ứng với tổng EXP. Dùng vòng lặp tăng dần thay vì giải phương trình bậc 2 bằng
    /// <c>Math.Sqrt</c>: ngay tại mốc (đúng 100 EXP) sai số dấu phẩy động có thể trả về level
    /// thấp hơn một bậc, và đó chính là chỗ user để ý nhất.
    /// </summary>
    public static int LevelForExp(int expPoints)
    {
        if (expPoints <= 0)
        {
            return 1;
        }

        var level = 1;
        while (level < MaxLevel && expPoints >= TotalExpForLevel(level + 1))
        {
            level++;
        }

        return level;
    }

    /// <summary>EXP còn thiếu để lên level kế tiếp; 0 nếu đã kịch level.</summary>
    public static int ExpToNextLevel(int expPoints)
    {
        var level = LevelForExp(expPoints);
        return level >= MaxLevel ? 0 : TotalExpForLevel(level + 1) - expPoints;
    }
}
