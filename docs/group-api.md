# Tài chính nhóm: API và phân quyền

Dành cho nhóm Flutter. Mọi route nằm dưới `/api/v1`, cần JWT, trả về envelope chung. Enum trong JSON là chuỗi PascalCase (`"Owner"`, `"ManageBudgets"`).

Công tắc: `config.features.groups` ở `GET /app-config`, mặc định `false`. App dùng nó để ẩn tính năng, và server cũng chặn thật: khi tắt, mọi API nhóm, `/group-invitations/*` và `PUT|DELETE /transactions/{id}/group` trả 422 `GROUPS_DISABLED`; tạo hoặc xác nhận giao dịch có `groupId` cũng vậy. Giao dịch không có `groupId` vẫn lưu bình thường và bỏ qua quy tắc tự chia sẻ. Sửa hoặc xóa giao dịch đã chia sẻ vẫn cập nhật số của nhóm. Cảnh báo nhóm tạm dừng và được gửi khi bật lại. Dữ liệu nhóm không mất.

**Phân trang con trỏ** (hoạt động, giao dịch chung, đóng góp mục tiêu): `limit` mặc định 20; `data` là mảng một trang, mới nhất trước; còn trang sau thì `meta.cursor` có giá trị, gửi lại nguyên văn qua `?cursor=` để lấy tiếp, hết thì `meta.cursor` là `null`. Con trỏ hỏng không báo lỗi mà trả trang đầu. App cũ chỉ đọc `data` sẽ chỉ thấy 20 dòng đầu.

## Vai trò và quyền

| Quyền | Owner | Manager | Member | Viewer |
|---|---|---|---|---|
| `ViewOverview` | ✅ | ✅ | ✅ | ✅ |
| `ViewMemberTransactions` | ✅ | ✅ | Khi `transactionVisibility = Full` | Khi `transactionVisibility = Full` |
| `ViewActivity` | ✅ | ✅ | ✅ | ❌ |
| `ShareTransactions` | ✅ | ✅ | ✅ | ❌ |
| `ContributeGoals` | ✅ | ✅ | ✅ | ❌ |
| `ManageSharedTransactions` | ✅ | ✅ | ❌ | ❌ |
| `ManageBudgets`, `ManageGoals`, `ManageCategories` | ✅ | ✅ | ❌ | ❌ |
| `InviteMembers`, `RemoveMembers` | ✅ | ✅, chỉ với vai trò thấp hơn | ❌ | ❌ |
| `RecordFundEntries` (Phase 85) | ✅ | ✅ | ✅ | ❌ |
| `ManageFunds` (Phase 85) | ✅ | ✅ | ❌ | ❌ |

- **Chỉ Owner, không cấp được cho ai:** đổi vai trò, chỉnh quyền từng người, đổi cài đặt nhóm, chuyển chủ, xóa nhóm.
- **Không ai có:** sửa số tiền hoặc xóa giao dịch của người khác; xem ví, số dư, nội dung thông báo gốc hoặc giao dịch chưa chia sẻ của người khác.
- **Quyền từng người:** `(mặc định theo vai trò + grantedPermissions) − revokedPermissions`. Dùng `myPermissions` trong `GroupDto` để ẩn hoặc hiện nút, đừng suy từ vai trò.
- **Lỗi:** không phải thành viên → 404; là thành viên nhưng thiếu quyền → 403.

## Nhóm

| Route | Quyền | Ghi chú |
|---|---|---|
| `POST /groups` `{name, type?}` | Đăng nhập | `type`: `Family` (mặc định) hoặc `Other`. Trả 201 |
| `GET /groups` | Đăng nhập | Các nhóm đang tham gia |
| `GET /groups/{id}` | Thành viên | |
| `PATCH /groups/{id}` `{name?, transactionVisibility?}` | Owner | `transactionVisibility`: `Full` hoặc `Summary` |
| `DELETE /groups/{id}` | Owner | Xóa mềm, 204 |
| `GET /groups/{id}/activities?cursor=&limit=` | `ViewActivity` | Mới nhất trước, phân trang con trỏ (xem dưới) |

## Thành viên

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /groups/{id}/members` | `ViewOverview` | `permissions`, `grantedPermissions`, `revokedPermissions` là `null` trừ khi xem chính mình hoặc người xem là Owner. `isActive = false`: tài khoản đã yêu cầu xóa, tên được thay bằng "Thành viên không còn hoạt động" |
| `PATCH /groups/{id}/members/{userId}` `{role?, grantedPermissions?, revokedPermissions?}` | Owner | Không đổi được chính mình; không gán `Owner` ở đây. Đổi vai trò thì xóa quyền ghi đè cũ, trừ khi gửi kèm quyền mới |
| `DELETE /groups/{id}/members/{userId}` | `RemoveMembers` | Chỉ vai trò thấp hơn mình |
| `POST /groups/{id}/leave` | Thành viên | Owner nhận 422 `GROUP_OWNER_MUST_TRANSFER` |
| `POST /groups/{id}/transfer-ownership` `{newOwnerUserId}` | Owner | Chủ cũ thành Manager |

## Lời mời

| Route | Quyền | Ghi chú |
|---|---|---|
| `POST /groups/{id}/invitations` `{email?, role?}` | `InviteMembers` | Không có `email`: trả `token` **một lần duy nhất** để app tự tạo link chia sẻ. Có `email`: gửi mã qua email, trả `emailSent`. `role`: `Manager`, `Member` (mặc định), `Viewer`, và phải thấp hơn vai trò người mời |
| `GET /groups/{id}/invitations` | `InviteMembers` | Lời mời đang chờ, không có mã |
| `DELETE /groups/{id}/invitations/{invitationId}` | `InviteMembers` | Thu hồi |
| `POST /group-invitations/preview` `{token}` | Đăng nhập | Tên nhóm, vai trò, người mời |
| `POST /group-invitations/accept` `{token}` | Đăng nhập | Trả `{groupId}` |

Mã sai, hết hạn, đã dùng, đã thu hồi hoặc dành cho email khác đều trả 422 `GROUP_INVITATION_INVALID`. Lời mời theo email đòi tài khoản đã xác minh đúng email đó. Hai route `group-invitations` có giới hạn tần suất chặt (429).

## Chia sẻ giao dịch

Ví và số dư không đổi; khoản đã chia sẻ vẫn tính vào ngân sách cá nhân của chủ giao dịch. Không chia sẻ được: nháp, chuyển khoản nội bộ, cho vay/mượn (422 `GROUP_TRANSACTION_NOT_SHAREABLE`).

| Route | Quyền | Ghi chú |
|---|---|---|
| `POST /transactions`, `POST /transactions/{id}/confirm` thêm `groupId?`, `groupCategoryId?` | `ShareTransactions` | Không gửi `groupId` thì áp quy tắc tự chia sẻ |
| `PUT /transactions/{id}/group` `{groupId, groupCategoryId?}` | Chủ giao dịch + `ShareTransactions` | Cũng dùng để chuyển nhóm hoặc đổi danh mục nhóm |
| `DELETE /transactions/{id}/group` | Chủ giao dịch, còn là thành viên | Người đã rời nhóm chỉ xóa được hẳn giao dịch |
| `GET /groups/{id}/transactions?year=&month=&cursor=&limit=` | `ViewOverview` | Mặc định tháng này, phân trang con trỏ (tối đa 200 một trang). Thiếu `ViewMemberTransactions` thì chỉ trả khoản của chính mình |
| `PATCH /groups/{id}/transactions/{transactionId}` `{groupCategoryId}` | `ManageSharedTransactions` | Không đổi dữ liệu cá nhân |
| `DELETE /groups/{id}/transactions/{transactionId}` | `ManageSharedTransactions` | Gỡ khỏi nhóm; giao dịch vẫn còn ở phía chủ |
| `GET/POST /groups/{id}/share-rules` `{financialAccountId?, categoryId?}`, `DELETE .../{ruleId}` | Của chính mình; tạo cần `ShareTransactions` | Bỏ trống cả hai là chia sẻ mọi khoản. Rời nhóm thì quy tắc tắt |

`TransactionDto` có thêm `groupId`. `GET /groups/{id}/transactions` cố ý không có ví và số dư.

## Danh mục, ngân sách, mục tiêu

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /groups/{id}/categories` | `ViewOverview` | |
| `POST /groups/{id}/categories` `{name, iconName?}`, `DELETE .../{categoryId}` | `ManageCategories` | Danh mục đang có ngân sách thì không xóa được |
| `GET /groups/{id}/budgets` | `ViewOverview` | Số liệu tháng này |
| `POST /groups/{id}/budgets` `{categoryId?, groupCategoryId?, limitCents}` | `ManageBudgets` | Bỏ trống cả hai là ngân sách tổng. `categoryId` chỉ nhận danh mục hệ thống |
| `PATCH /groups/{id}/budgets/{budgetId}` `{limitCents}`, `DELETE ...` | `ManageBudgets` | |
| `GET /groups/{id}/goals`, `GET .../{goalId}/contributions?cursor=&limit=` | `ViewOverview` | Đóng góp: phân trang con trỏ |
| `POST /groups/{id}/goals` `{name, targetCents, deadline?}`, `PATCH .../{goalId}`, `POST .../{goalId}/cancel` | `ManageGoals` | |
| `POST /groups/{id}/goals/{goalId}/contribute` `{amountCents, note?}` | `ContributeGoals` | Chỉ ghi sổ, không trừ ví |
| `PUT /groups/{id}/alerts` `{enabled}` | Thành viên | Tự bật hoặc tắt cảnh báo của nhóm này cho mình |

| `GET /groups/{id}/reports/monthly?year=&month=` | `ViewOverview` | Tổng thu chi, `byCategory`, `byMember`; chế độ `Summary` vẫn hiện tổng |

Push mới: `data.kind` là `group_budget` (`groupId`, `budgetId`, `percent`) hoặc `group_goal` (`groupId`, `goalId`). Cả hai cũng hiện trong `GET /forecast/alerts`.

## Quỹ chung (Phase 85)

Quỹ là sổ quỹ **ảo** thuộc về nhóm: FinMate không giữ tiền, chỉ ghi sổ, ví cá nhân không bị chia sẻ. Cần cả `features.groups` và `features.groupFunds` (mặc định `false`, có trong `GET /app-config`). Cờ nhóm tắt thì mọi route quỹ trả 422 `GROUPS_DISABLED`; cờ nhóm bật mà cờ quỹ tắt thì 422 `GROUP_FUNDS_DISABLED`. Tắt cờ quỹ không làm số lệch: xoá giao dịch cá nhân gắn quỹ vẫn xoá dòng quỹ, chi từ quỹ vẫn tính vào ngân sách nhóm. Mọi route dưới `/api/v1/groups/{groupId}`.

### Quỹ

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /funds` | `ViewOverview` | Quỹ đang dùng trước, rồi đến quỹ đã lưu trữ |
| `GET /funds/{fundId}` | `ViewOverview` | `{id, name, note, balanceCents, isNegative, isArchived, archivedAt, createdAt, updatedAt}` |
| `POST /funds` `{name, note?}` | `ManageFunds` | 201. Tối đa `groups.maxFundsPerGroup` (mặc định 3) quỹ **chưa lưu trữ**: vượt thì 422 `GROUP_TOO_MANY_FUNDS` |
| `PATCH /funds/{fundId}` `{name?, note?}` | `ManageFunds` | Làm được cả khi đã lưu trữ. `note = ""` là xoá ghi chú |
| `POST /funds/{fundId}/archive` | `ManageFunds` | Chỉ khi số dư = 0, không thì 422 `GROUP_FUND_NOT_SETTLED`. Quỹ đã lưu trữ chỉ đọc (ghi, sửa, xoá dòng → 422 `GROUP_FUND_ARCHIVED`). Không có bỏ lưu trữ |
| `GET /funds/{fundId}/summary?year=&month=` | `ViewOverview` | Mặc định tháng này (giờ VN). `{openingCents, contributedCents, contributionsByMember[], spentCents, expensesByCategory[], withdrawnCents, withdrawalsByMember[], adjustmentsCents, closingCents}` |

Số dư quỹ = Σ góp − Σ chi − Σ rút ± điều chỉnh, **được phép âm** (có người ứng trước; `isNegative = true`). `DELETE /groups/{id}` khi còn quỹ chưa lưu trữ → 422 `GROUP_FUND_NOT_SETTLED`.

### Dòng quỹ

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /funds/{fundId}/entries?cursor=&limit=&kind=&memberId=` | `ViewOverview` | Phân trang con trỏ như trên. Hiện với mọi thành viên **kể cả chế độ `Summary`**: sổ quỹ là của nhóm |
| `POST /funds/{fundId}/entries` | Xem dưới | 201, hoặc 200 khi gửi lại cùng `clientRequestId`. Trả `{entry, fund, alreadyExisted}` |
| `PATCH /funds/{fundId}/entries/{entryId}` | Xem dưới | Trường bỏ trống là giữ nguyên. Trả `{entry, fund}` |
| `DELETE /funds/{fundId}/entries/{entryId}` | Xem dưới | Trả quỹ sau khi xoá |

Body của `POST`: `{kind, amountCents, transactedAt?, categoryId?, groupCategoryId?, note?, merchantName?, financialAccountId?, fromTransactionId?, clientRequestId?}`.

| `kind` | Quyền | Số tiền | Kèm theo |
|---|---|---|---|
| `Contribution` (góp) | `RecordFundEntries` | > 0, cộng vào quỹ | `financialAccountId?`: tạo giao dịch cá nhân tiền **ra** khỏi ví đó |
| `Expense` (chi từ quỹ) | `RecordFundEntries` | > 0, trừ quỹ | Bắt buộc `categoryId` (danh mục **hệ thống**, không phải cho vay/mượn) hoặc `groupCategoryId`; `merchantName?`; `financialAccountId?` (tiền ra khỏi ví) **hoặc** `fromTransactionId?` (xem dưới) |
| `Withdrawal` (rút) | `RecordFundEntries` | > 0, trừ quỹ | `financialAccountId?`: giao dịch cá nhân tiền **vào** ví đó |
| `Adjustment` (điều chỉnh) | `ManageFunds` | ≠ 0, theo dấu | `note` bắt buộc; không kèm ví |

- Dòng luôn đứng tên **người gọi** (`memberUserId`). Sai hình dạng (chi thiếu danh mục, loại khác gửi danh mục, điều chỉnh kèm ví…) → 422 `GROUP_FUND_ENTRY_INVALID`. Ví của người khác → 404.
- `fromTransactionId`: chuyển một khoản **chi đã xác nhận** của chính người ghi thành khoản chi từ quỹ (vd. thông báo VCB −800k EVN mà người giữ quỹ đã xác nhận). Số tiền và thời điểm lấy từ giao dịch; danh mục lấy từ giao dịch nếu là danh mục hệ thống, không thì phải gửi. Giao dịch không đủ điều kiện (nháp, đã chia sẻ, là hoàn tiền, đã có khoản hoàn, cho vay/mượn, đang nghi trùng, đã gắn quỹ) → 422 `GROUP_FUND_SOURCE_NOT_ELIGIBLE`. Ví không đổi; chi tiêu cá nhân không còn tính khoản đó.
- **Sửa:** chủ dòng sửa mọi trường trừ `kind` (cả đổi ví, thêm ví, `removeFinancialAccount: true`). Người có `ManageFunds` chỉ sửa `categoryId`, `groupCategoryId`, `note` của dòng người khác. **Không ai sửa số tiền dòng của người khác** (403). Dòng lấy từ giao dịch không đổi được số tiền, thời điểm, ví (422 `GROUP_FUND_ENTRY_LOCKED`).
- **Xoá:** chủ dòng xoá → giao dịch cá nhân gắn với dòng bị xoá mềm và ví được hoàn (dòng lấy từ giao dịch thì giao dịch **về lại khoản chi cũ** với danh mục gốc). Người có `ManageFunds` xoá dòng của người khác → **chỉ** xoá dòng quỹ; giao dịch cá nhân của chủ dòng giữ nguyên (`groupFundLink.entryDeleted = true`) và chủ dòng nhận push `group_fund_removed`.

### Phía sổ cá nhân

- Giao dịch gắn quỹ là `Transfer` với `counterAccountId = null` và `groupFundLink: {groupId, fundId, fundName, kind, direction, entryDeleted}` (`direction`: `out` | `in`). **Không** cộng vào chi hay thu ở đâu cả (báo cáo, ngân sách, hũ, dự báo). Hiện như "Góp quỹ" / "Nhận từ quỹ" / "Chi từ quỹ", không như chuyển khoản. `groupFundLink` vẫn trả sau khi rời nhóm.
- Sửa, chuyển ví, hoàn tiền, liên kết hoàn, chia sẻ nhóm, làm bên thua khi gộp → 422 `TRANSACTION_GROUP_FUND_LINKED`. Sửa ở dòng quỹ.
- `DELETE /transactions/{id}` → dòng quỹ bị xoá theo, trừ khi người đó đã rời nhóm hoặc quỹ đã lưu trữ (khi đó chỉ giao dịch cá nhân bị xoá, dòng quỹ ở lại).
- `POST /transactions/{id}/restore-from-fund`: sau khi người quản lý xoá một dòng chi **lấy từ giao dịch**, chủ dòng đưa giao dịch về lại khoản chi. Dòng còn đó hoặc không phải dòng lấy từ giao dịch → 422 `TRANSACTION_GROUP_FUND_LINKED`.
- Thông báo ngân hàng trùng với một giao dịch góp quỹ có thể gộp vào nó; giao dịch gắn quỹ luôn là bên thắng.
- **Hạn chế (chưa làm):** tiền góp quỹ chuyển vào tài khoản ngân hàng của người giữ quỹ đi qua thông báo ngân hàng thành **thu nhập** của người giữ quỹ. Phase sau sẽ ghép thông báo đó với dòng góp.

### Báo cáo tháng của nhóm

`GET /groups/{id}/reports/monthly` cộng thêm khoản chi từ quỹ vào `totalSpentCents` và `byCategory`, đứng tên người ghi trong `byMember`. Trường mới, đều tuỳ chọn: `fundSpentCents` (phần chi từ quỹ, ở tổng và ở từng người), `formerMembers {totalSpentCents, fundSpentCents}` (phần của người đã xoá cứng tài khoản; `null` khi không có). `byMember` chỉ gồm người thật, `userId` không bao giờ `null`. Góp, rút, điều chỉnh không vào báo cáo tháng.

### Rời nhóm, xoá tài khoản

Dòng quỹ thuộc về nhóm. Rời hoặc bị xoá khỏi nhóm: dòng ở lại, vẫn ghi tên; người đã rời nhận 404 ở mọi API quỹ; giao dịch cá nhân vẫn ở sổ của họ. Đang chờ xoá tài khoản: tên hiện "Thành viên không còn hoạt động". Xoá cứng: dòng **không bị xoá** mà thành "Thành viên cũ" (`memberUserId = null`), vì xoá sẽ làm sai số dư quỹ cả nhóm đang dựa vào; số dư quỹ và ngân sách nhóm không đổi.

### Push

`data.kind = group_fund_negative` (`groupId`, `fundId`): quỹ vừa chuyển sang âm, gửi một lần mỗi đợt âm cho người có `ManageFunds` đang bật cảnh báo nhóm. `group_fund_removed` (`groupId`, `fundId`, `entryId`): người quản lý xoá dòng của bạn. Không số tiền. Cả hai hiện trong `GET /forecast/alerts`.

## Xóa tài khoản

- Yêu cầu xóa: quyền chủ nhóm tự chuyển ngay. Trong 30 ngày chờ, người đó có `isActive = false` và tên hiển thị "Thành viên không còn hoạt động".
- Xóa cứng: giao dịch đã chia sẻ và khoản góp của họ biến mất, số liệu nhóm được tính lại, nên báo cáo tháng cũ có thể đổi.
- Rời nhóm thì khác: lịch sử giữ nguyên và vẫn ghi tên.

## Điều khoản

`ConsentVersions.Terms` là 4. Mọi người phải đồng ý lại, và đồng ý gửi `version = 3` bị từ chối (409 `AUTH_CONSENT_VERSION_OUTDATED`). App cần có câu chữ mới về việc giao dịch đã chia sẻ (số tiền, nơi chi, ghi chú, tên) hiện với thành viên nhóm và còn đó khi rời nhóm. Bản 4 còn phải nêu hai việc: ảnh hóa đơn được gửi nguyên bản, chưa che dữ liệu cá nhân, tới Google Cloud Vision để đọc chữ; và việc lưu ảnh trong ticket, nhân viên xem dữ liệu khi người dùng đồng ý (xem `docs/ops-api.md`).

## Mã lỗi

`GROUP_TOO_MANY_GROUPS`, `GROUP_TOO_MANY_MEMBERS`, `GROUP_TOO_MANY_INVITATIONS`, `GROUP_ALREADY_MEMBER` (409), `GROUP_MEMBERSHIP_CONFLICT` (409, gửi lại), `GROUP_OWNER_MUST_TRANSFER`, `GROUP_CANNOT_CHANGE_SELF`, `GROUP_INVALID_OWNER_ASSIGNMENT`, `GROUP_INVITATION_INVALID`, `CONCURRENT_UPDATE` (409, gửi lại)., `GROUP_TRANSACTION_NOT_SHAREABLE`, `GROUP_BUDGET_ALREADY_EXISTS` (409), `GROUP_BUDGET_INVALID_CATEGORY`, `GROUP_CATEGORY_DUPLICATE` (409), `GROUP_CATEGORY_IN_USE` (409), `GROUP_GOAL_NOT_ACTIVE`.

Quỹ (Phase 85, đều 422 trừ khi ghi khác): `GROUP_FUNDS_DISABLED`, `GROUP_TOO_MANY_FUNDS`, `GROUP_FUND_NOT_SETTLED`, `GROUP_FUND_ARCHIVED`, `GROUP_FUND_ENTRY_INVALID`, `GROUP_FUND_ENTRY_LOCKED`, `GROUP_FUND_SOURCE_NOT_ELIGIBLE`, `TRANSACTION_GROUP_FUND_LINKED`, `CONCURRENT_UPDATE` (409, gửi lại).
