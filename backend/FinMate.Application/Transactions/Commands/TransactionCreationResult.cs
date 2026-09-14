using FinMate.Application.Common.Models;

namespace FinMate.Application.Transactions.Commands;

/// <param name="AlreadyExisted">
/// True khi <c>clientRequestId</c> khớp một giao dịch đã tạo trước đó — tức là lần gửi này là
/// GỬI LẠI, không phải tạo mới. Controller dùng nó để trả 200 thay vì 201: trả 201 cho một
/// request không tạo ra gì là nói sai với client, và client nào đếm số giao dịch đã đồng bộ
/// theo mã 201 sẽ đếm nhầm.
/// </param>
public record TransactionCreationResult(TransactionDto Transaction, bool AlreadyExisted);
