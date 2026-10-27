namespace FinMate.Domain.Enums;

/// <summary>Gợi ý do AI bên thứ ba chọn, hay do xếp hạng bằng rule khi AI lỗi hoặc trả sai.</summary>
public enum RecommendationSource
{
    Ai,
    RuleFallback,
}
