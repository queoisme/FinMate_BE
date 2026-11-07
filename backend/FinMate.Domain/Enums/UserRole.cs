namespace FinMate.Domain.Enums;

public enum UserRole
{
    User,
    Admin,

    /// <summary>Quản lý: duyệt giả định / tri thức, xử lý khiếu nại đã leo thang. Không quản lý người dùng hay cấu hình hệ thống (việc của Admin).</summary>
    Manager,

    /// <summary>Nhân sự hỗ trợ khách hàng: xem và trả lời khiếu nại.</summary>
    SupportStaff,

    /// <summary>Nhân sự nội dung: soạn giả định kinh tế và tài liệu tri thức (bản nháp); duyệt là việc của Manager trở lên.</summary>
    ContentStaff,
}
