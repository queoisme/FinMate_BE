namespace FinMate.Domain.Enums;

public enum SavingGoalStatus
{
    Active,
    Completed,
    Cancelled,

    // Phải đứng SAU Active: EF coi giá trị 0 của enum là "chưa đặt" và dùng mặc định của cột (active) nên một Draft = 0 sẽ bị ghi thành active.
    /// <summary>Bản nháp: lưu để làm tiếp, chưa tính vào kế hoạch, dự báo hay nhắc hạn; kích hoạt bằng <c>POST /saving-goals/{id}/activate</c>.</summary>
    Draft,
}
