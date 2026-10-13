namespace FinMate.Application.Auth.Commands;

/// <param name="Version">Phiên bản câu chữ mà app đã hiển thị cho người dùng; phải bằng phiên bản hiện hành của server.</param>
public record AcceptConsentCommand(Guid UserId, ConsentKind Kind, int Version);
