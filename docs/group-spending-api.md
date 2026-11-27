# Nhóm chi tiêu: hồ sơ nhóm, lời mời, hóa đơn chia, sổ nợ, duyệt khoản lớn

Dành cho nhóm Flutter. Bổ sung cho `docs/group-api.md` (vai trò, quyền, quỹ chung). Mọi route nằm dưới `/api/v1`, cần JWT, trả envelope chung `{success, data, error, meta}`. Enum trong JSON là chuỗi PascalCase. Tiền luôn là số nguyên đồng (`*Cents`), id là GUID, thời gian là ISO 8601 có múi giờ.

Mọi ví dụ bên dưới lấy từ **một lần chạy thật** các luồng này trên backend + Postgres thật; mỗi lần gọi cũng được lưu thành một file riêng trong `docs/fixtures/group-spending/<tên>.json` (`{method, url, request, status, response}`). Id, tên và thời gian trong ví dụ sẽ khác ở máy bạn.

Tất cả route nhóm dưới đây cần cờ `features.groups` (tắt thì 422 `GROUPS_DISABLED`). Route quỹ cần thêm `features.groupFunds`.

## Quy ước chung

- **Lỗi kiểm tra dữ liệu** (body sai hình dạng, quá dài…) là **400** `VALIDATION_ERROR` kèm `error.details[{field, message}]`. **Lỗi nghiệp vụ** là **422** với mã cố định, hoặc 404/403/409/429 như ghi ở từng route. Không phải thành viên nhóm thì luôn **404**; là thành viên nhưng thiếu quyền thì **403**.
- **429 của nghiệp vụ** (`GROUP_REMINDER_COOLDOWN`, `USER_SEARCH_RATE_LIMITED`) khác 429 của giới hạn chung: body có `error.code` cố định, chưa có header `Retry-After`.
- **202 Accepted** nghĩa là yêu cầu đã nhận nhưng **chưa có hiệu lực** (đang chờ chủ nhóm duyệt). Xem mục "Duyệt khoản lớn": body khi đó là một yêu cầu chờ duyệt, không phải hóa đơn hay dòng quỹ. App phải kiểm tra **mã HTTP** chứ không chỉ `success`.
- Các số liệu trên thẻ danh sách (`card`) chỉ có ở `GET /groups`, không có ở `GET /groups/{id}`.

## 1. Nhóm: hồ sơ, thẻ danh sách, cài đặt

### Loại nhóm

`type`: `Family` (mặc định), `Friends`, `Travel`, `Colleagues`, `Roommates`, `Other`.

### Tạo và sửa

`POST /groups` nhận thêm các trường tuỳ chọn; `PATCH /groups/{id}` (chỉ Owner) nhận cùng bộ trường, trường bỏ trống là giữ nguyên.

| Trường | Ghi chú |
|---|---|
| `icon` | Tên biểu tượng, tối đa 50 ký tự. Chuỗi rỗng là xoá |
| `description` | Tối đa 200 ký tự |
| `currency` | Hiện chỉ nhận `VND` (mặc định) |
| `settlementModel` | `SharedFund` (mặc định) hoặc `SplitBill` |
| `closingDay` | 1–28, bỏ trống là cuối tháng. **PATCH không đặt lại về cuối tháng được**: dùng `PUT /settings` |
| `debtSettlementMethod` | `AiOptimized` (mặc định) hoặc `Manual` |
| `avatarUrl` | **Luôn `null` hiện nay**: upload ảnh nhóm chưa làm |

```http
POST /api/v1/groups

{
  "name": "Phòng trọ 302",
  "type": "Roommates",
  "icon": "home",
  "description": "Chia tiền điện nước hằng tháng",
  "currency": "VND",
  "settlementModel": "SplitBill",
  "closingDay": 5,
  "debtSettlementMethod": "AiOptimized"
}

→ 201
{
  "success": true,
  "data": {
    "id": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "name": "Phòng trọ 302",
    "type": "Roommates",
    "transactionVisibility": "Full",
    "myRole": "Owner",
    "myPermissions": [
      "ViewOverview",
      "ViewMemberTransactions",
      "ViewActivity",
      "ShareTransactions",
      "ContributeGoals",
      "ManageSharedTransactions",
      "ManageBudgets",
      "ManageGoals",
      "ManageCategories",
      "InviteMembers",
      "RemoveMembers",
      "ManageFunds",
      "RecordFundEntries"
    ],
    "memberCount": 1,
    "createdAt": "2026-11-20T10:18:42.3883745+00:00",
    "icon": "home",
    "avatarUrl": null,
    "description": "Chia tiền điện nước hằng tháng",
    "currency": "VND",
    "settlementModel": "SplitBill",
    "closingDay": 5,
    "debtSettlementMethod": "AiOptimized",
    "myDefaultFinancialAccountId": null,
    "card": null
  },
  "error": null,
  "meta": null
}
```

```http
PATCH /api/v1/groups/{id}

{
  "description": "Điện nước + internet",
  "icon": "house"
}

→ 200
{
  "success": true,
  "data": {
    "id": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "name": "Phòng trọ 302",
    "type": "Roommates",
    "transactionVisibility": "Full",
    "myRole": "Owner",
    "myPermissions": [
      "ViewOverview",
      "ViewMemberTransactions",
      "ViewActivity",
      "ShareTransactions",
      "ContributeGoals",
      "ManageSharedTransactions",
      "ManageBudgets",
      "ManageGoals",
      "ManageCategories",
      "InviteMembers",
      "RemoveMembers",
      "ManageFunds",
      "RecordFundEntries"
    ],
    "memberCount": 1,
    "createdAt": "2026-11-20T10:18:42.388374+00:00",
    "icon": "house",
    "avatarUrl": null,
    "description": "Điện nước + internet",
    "currency": "VND",
    "settlementModel": "SplitBill",
    "closingDay": 5,
    "debtSettlementMethod": "AiOptimized",
    "myDefaultFinancialAccountId": null,
    "card": null
  },
  "error": null,
  "meta": null
}
```


`GroupDto` có thêm `icon`, `avatarUrl`, `description`, `currency`, `settlementModel`, `closingDay`, `debtSettlementMethod` và `myDefaultFinancialAccountId` (ví nguồn tiền mặc định của chính người xem, xem mục Lời mời).

### Thẻ danh sách `GET /groups`

Mỗi nhóm có thêm `card`:

| Trường | Ý nghĩa |
|---|---|
| `fundBalanceCents` | Tổng số dư các quỹ chưa lưu trữ; `null` khi nhóm chưa có quỹ |
| `monthlySpentCents` | Tổng chi tháng này của nhóm (giao dịch chia sẻ + chi từ quỹ + hóa đơn chia) |
| `monthlyBudgetCents` | Hạn mức ngân sách tổng của nhóm; `null` khi chưa đặt |
| `memberPreview` | Tối đa 3 thành viên: `userId`, `displayName`, `avatarUrl` (hiện luôn `null`, tài khoản chưa có ảnh đại diện) |

```http
GET /api/v1/groups

→ 200
{
  "success": true,
  "data": [
    {
      "id": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "name": "Phòng trọ 302",
      "type": "Roommates",
      "transactionVisibility": "Full",
      "myRole": "Owner",
      "myPermissions": [
        "ViewOverview",
        "ViewMemberTransactions",
        "ViewActivity",
        "ShareTransactions",
        "ContributeGoals",
        "ManageSharedTransactions",
        "ManageBudgets",
        "ManageGoals",
        "ManageCategories",
        "InviteMembers",
        "RemoveMembers",
        "ManageFunds",
        "RecordFundEntries"
      ],
      "memberCount": 4,
      "createdAt": "2026-11-20T10:18:42.388374+00:00",
      "icon": "house",
      "avatarUrl": null,
      "description": "Điện nước + internet",
      "currency": "VND",
      "settlementModel": "SplitBill",
      "closingDay": 5,
      "debtSettlementMethod": "AiOptimized",
      "myDefaultFinancialAccountId": null,
      "card": {
        "fundBalanceCents": 10000000,
        "monthlySpentCents": 7600000,
        "monthlyBudgetCents": 20000000,
        "memberPreview": [
          {
            "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
            "displayName": "Minh",
            "avatarUrl": null
          },
          {
            "userId": "3026b293-a99a-4408-a214-75924c9f549f",
            "displayName": "Lan",
            "avatarUrl": null
          },
          {
            "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
            "displayName": "Hung",
            "avatarUrl": null
          }
        ]
      }
    },
    {
      "id": "457571b5-1966-44f3-a04f-3700d3d13446",
      "name": "Đi Đà Lạt",
      "type": "Travel",
      "transactionVisibility": "Full",
      "myRole": "Owner",
      "myPermissions": [
        "ViewOverview",
        "ViewMemberTransactions",
        "ViewActivity",
        "ShareTransactions",
        "ContributeGoals",
        "ManageSharedTransactions",
        "ManageBudgets",
        "ManageGoals",
        "ManageCategories",
        "InviteMembers",
        "RemoveMembers",
        "ManageFunds",
        "RecordFundEntries"
      ],
      "memberCount": 1,
      "createdAt": "2026-11-20T10:18:43.767984+00:00",
      "icon": null,
      "avatarUrl": null,
      "description": null,
      "currency": "VND",
      "settlementModel": "SharedFund",
      "closingDay": null,
      "debtSettlementMethod": "AiOptimized",
      "myDefaultFinancialAccountId": null,
      "card": {
        "fundBalanceCents": null,
        "monthlySpentCents": 0,
        "monthlyBudgetCents": null,
        "memberPreview": [
          {
            "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
            "displayName": "Minh",
            "avatarUrl": null
          }
        ]
      }
    }
  ],
  "error": null,
  "meta": null
}
```


### Cài đặt nhóm

`GET /groups/{id}/settings` (mọi thành viên), `PUT /groups/{id}/settings` (**chỉ Owner**, 403 với người khác). PUT **thay toàn bộ**: `closingDay: null` là cuối tháng, `requireApprovalAboveCents: null` là tắt duyệt.

| Trường | Ghi chú |
|---|---|
| `closingDay` | 1–28 hoặc `null` |
| `debtSettlementMethod` | `AiOptimized` / `Manual` |
| `requireApprovalAboveCents` | > 0 hoặc `null`. Xem mục Duyệt khoản lớn |
| `notifyOnNewTransaction`, `weeklySummary` | Lưu và trả lại; **server chưa gửi thông báo theo hai cờ này** (chưa làm) |
| `weeklySummaryDay` | 1 (thứ Hai) … 7 (Chủ nhật) |
| `weeklySummaryTime` | `HH:mm` giờ Việt Nam |
| `alertsEnabled` | Chỉ đọc: cảnh báo của chính người xem (`PUT /groups/{id}/alerts` để đổi) |

```http
GET /api/v1/groups/{id}/settings

→ 200
{
  "success": true,
  "data": {
    "closingDay": 5,
    "debtSettlementMethod": "AiOptimized",
    "requireApprovalAboveCents": null,
    "notifyOnNewTransaction": true,
    "weeklySummary": false,
    "weeklySummaryDay": 1,
    "weeklySummaryTime": "09:00",
    "alertsEnabled": true
  },
  "error": null,
  "meta": null
}
```

```http
PUT /api/v1/groups/{id}/settings

{
  "closingDay": 5,
  "debtSettlementMethod": "AiOptimized",
  "requireApprovalAboveCents": 5000000,
  "notifyOnNewTransaction": true,
  "weeklySummary": true,
  "weeklySummaryDay": 7,
  "weeklySummaryTime": "20:30"
}

→ 200
{
  "success": true,
  "data": {
    "closingDay": 5,
    "debtSettlementMethod": "AiOptimized",
    "requireApprovalAboveCents": 5000000,
    "notifyOnNewTransaction": true,
    "weeklySummary": true,
    "weeklySummaryDay": 7,
    "weeklySummaryTime": "20:30",
    "alertsEnabled": true
  },
  "error": null,
  "meta": null
}
```


## 2. Lời mời

Có hai cách mời: **qua link** (không gửi `email`) và **qua email** (gửi `email`). Lời mời gửi thẳng tới một tài khoản (theo userId hoặc FinMate ID) **không có**.

| Cách | Body `POST /groups/{id}/invitations` | Người nhận nhận bằng |
|---|---|---|
| Link | `{role?, message?}` | `token` (trả **một lần** kèm `joinUrl`) |
| Email | `{email, role?, message?}` | `token` gửi qua email |

`message` tối đa 200 ký tự (400 nếu dài hơn). Quyền: `InviteMembers`, và vai trò được mời phải thấp hơn vai trò của người mời.

**Link một lần:** lời mời qua link (`POST /groups/{id}/invitations` không có `email`) trả `joinUrl` = `https://finmate.app/join/<token>` (phần đầu lấy từ cấu hình hệ thống `groups.inviteLinkBaseUrl`, phải là https). Backend chỉ tạo URL; tên miền và deep link do hạ tầng/app lo. Để vào nhóm, app lấy `<token>` từ link và gọi `accept`.

```http
POST /api/v1/groups/{id}/invitations

{
  "role": "Member",
  "message": "Vào nhóm chia tiền phòng nhé!"
}

→ 201
{
  "success": true,
  "data": {
    "id": "4769dab1-db91-418e-aa10-effb4b71f428",
    "email": null,
    "role": "Member",
    "expiresAt": "2026-11-27T10:18:42.8617231+00:00",
    "createdAt": "2026-11-20T10:18:42.8617231+00:00",
    "token": "CEt2o7mLh64Muab0EOfByDryM0LP9KZooNilyfaEn-4",
    "emailSent": null,
    "message": "Vào nhóm chia tiền phòng nhé!",
    "joinUrl": "https://finmate.app/join/CEt2o7mLh64Muab0EOfByDryM0LP9KZooNilyfaEn-4"
  },
  "error": null,
  "meta": null
}
```


`GET /groups/{id}/invitations` (người có `InviteMembers`) liệt kê lời mời đang chờ (không có mã).

```http
GET /api/v1/groups/{id}/invitations

→ 200
{
  "success": true,
  "data": [
    {
      "id": "be672a16-242b-490e-b2f8-41448bdb7c0d",
      "email": null,
      "role": "Member",
      "expiresAt": "2026-11-27T10:18:43.498451+00:00",
      "createdAt": "2026-11-20T10:18:43.498451+00:00",
      "token": null,
      "emailSent": null,
      "message": "Mai vào nhóm nha",
      "joinUrl": null
    }
  ],
  "error": null,
  "meta": null
}
```


### Xem trước

`POST /group-invitations/preview` nhận `{token}`. Trả thêm thông tin để vẽ thẻ lời mời:

| Trường | Ghi chú |
|---|---|
| `groupIcon`, `memberCount` | |
| `fundBalanceCents` | Tổng số dư quỹ chưa lưu trữ; `null` khi chưa có quỹ |
| `invitedByRole` | Vai trò của người mời; `null` nếu người đó đã rời nhóm |
| `message` | Lời nhắn (≤ 200 ký tự) |

```http
POST /api/v1/group-invitations/preview

{
  "token": "CEt2o7mLh64Muab0EOfByDryM0LP9KZooNilyfaEn-4"
}

→ 200
{
  "success": true,
  "data": {
    "groupName": "Phòng trọ 302",
    "groupType": "Roommates",
    "role": "Member",
    "invitedBy": "Minh",
    "expiresAt": "2026-11-27T10:18:42.861723+00:00",
    "groupIcon": "house",
    "memberCount": 1,
    "fundBalanceCents": null,
    "invitedByRole": "Owner",
    "message": "Vào nhóm chia tiền phòng nhé!"
  },
  "error": null,
  "meta": null
}
```


### Nhận và từ chối

`POST /group-invitations/accept` nhận `{token, defaultFinancialAccountId?}`. `defaultFinancialAccountId` phải là ví **của chính người dùng**, không thì 422 `GROUP_DEFAULT_ACCOUNT_INVALID`. Giá trị được lưu và trả lại ở `myDefaultFinancialAccountId`; **server không tự dùng nó** khi góp quỹ hay ghi chi: app tự truyền `financialAccountId` vào dòng quỹ.

`POST /group-invitations/decline` nhận `{token}`: 404 khi mã không có thật hoặc dành cho email khác, 422 `GROUP_INVITATION_INVALID` khi đã xử lý hoặc hết hạn. Lời mời bị từ chối biến khỏi danh sách đang chờ; mời lại được.

```http
POST /api/v1/group-invitations/accept

{
  "token": "CEt2o7mLh64Muab0EOfByDryM0LP9KZooNilyfaEn-4",
  "defaultFinancialAccountId": "d8816686-2440-49ee-a601-582b99263a32"
}

→ 200
{
  "success": true,
  "data": {
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/group-invitations/decline

{
  "token": "1rmNXgyShNIMyOUWV_tSH4cY1GfgMJO6mr0N-2FmPSU"
}

→ 200
{
  "success": true,
  "data": {
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "declinedAt": "2026-11-20T10:18:43.3070826+00:00"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/group-invitations/decline

{
  "token": "1rmNXgyShNIMyOUWV_tSH4cY1GfgMJO6mr0N-2FmPSU"
}

→ 422
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_INVITATION_INVALID",
    "message": "Lời mời đã được xử lý hoặc đã hết hạn.",
    "details": null
  },
  "meta": null
}
```

```http
POST /api/v1/group-invitations/decline

{
  "token": "khong-co-that"
}

→ 404
{
  "success": false,
  "data": null,
  "error": {
    "code": "NOT_FOUND",
    "message": "GroupInvitation với mã 'token' không tồn tại.",
    "details": null
  },
  "meta": null
}
```


Mã sai, hết hạn, đã dùng, đã thu hồi hoặc dành cho người khác ở `accept`/`preview` đều trả 422 `GROUP_INVITATION_INVALID` (cố ý chỉ một mã).

### Thu hồi tất cả

`DELETE /groups/{id}/invitations` (`InviteMembers`): thu hồi mọi lời mời đang chờ (link và email), trả `{revoked: n}`; không có gì để thu hồi thì `0`.

```http
DELETE /api/v1/groups/{id}/invitations

→ 200
{
  "success": true,
  "data": {
    "revoked": 2
  },
  "error": null,
  "meta": null
}
```


### Mã và link tham gia chung của nhóm

Mỗi nhóm có **một mã/link chung luôn có sẵn**: không hết hạn, dùng được nhiều lần, ai có mã đều vào nhóm với vai trò **Member**. App chỉ cần gọi `GET` khi mở màn Mời để hiện QR và link, không có nút "tạo".

- `GET /groups/{id}/join-link`: trả `{code, joinUrl, role, enabled}`. Lần đầu mở thì server tạo mã luôn; các lần sau trả đúng mã đó. Cần quyền `InviteMembers` (Member thường 403).
- `POST /groups/{id}/join-link/reset`: đổi mã mới, mã cũ hết dùng được, đồng thời bật lại nếu đang tắt. Dùng khi mã bị lộ.
- `PUT /groups/{id}/join-link` `{enabled}`: tắt/bật. Tắt thì không ai vào được bằng mã; bật lại dùng đúng mã cũ.
- Người vào dùng **đúng hai route cũ** với `token = code` (hoặc lấy `code` từ cuối link `.../join/<code>`): `POST /group-invitations/preview` và `POST /group-invitations/accept`. Preview của mã chung có `expiresAt = null` (không hết hạn), `invitedBy = null`, `invitedByRole = null`, `message = null`. `decline` không áp dụng cho mã chung (không có lời mời nào để từ chối).
- Mã sai, đã đặt lại hoặc đang tắt: 422 `GROUP_INVITATION_INVALID` (cố ý chỉ một mã). Đã là thành viên: 409 `GROUP_ALREADY_MEMBER`. Vẫn áp giới hạn số thành viên và số nhóm mỗi người.
- Thu hồi tất cả lời mời (`DELETE /groups/{id}/invitations`) **không** đụng tới mã chung. Lời mời qua email vẫn chọn vai trò, có hạn, dùng một lần.
- **Lưu ý bảo mật:** vì mã luôn hiện lại được nên server lưu nguyên văn (không băm như token lời mời); ai có link thì vào được cho tới khi chủ nhóm tắt hoặc đặt lại. Đây là đánh đổi cho tính tiện dụng, FE nên cho người dùng thấy rõ nút tắt/đặt lại.

```http
GET /api/v1/groups/{id}/join-link

→ 200
{
  "success": true,
  "data": {
    "code": "6R1W-xlxHY3V04gaiB2-EA",
    "joinUrl": "https://finmate.app/join/6R1W-xlxHY3V04gaiB2-EA",
    "role": "Member",
    "enabled": true
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/group-invitations/preview

{
  "token": "6R1W-xlxHY3V04gaiB2-EA"
}

→ 200
{
  "success": true,
  "data": {
    "groupName": "Phòng trọ 302",
    "groupType": "Roommates",
    "role": "Member",
    "invitedBy": null,
    "expiresAt": null,
    "groupIcon": "home",
    "memberCount": 1,
    "fundBalanceCents": null,
    "invitedByRole": null,
    "message": null
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/group-invitations/accept

{
  "token": "6R1W-xlxHY3V04gaiB2-EA"
}

→ 200
{
  "success": true,
  "data": {
    "groupId": "508464f9-dfa5-4faa-923e-8d8e515f3dc5"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/group-invitations/accept

{
  "token": "6R1W-xlxHY3V04gaiB2-EA"
}

→ 409
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_ALREADY_MEMBER",
    "message": "Bạn đã là thành viên của nhóm này.",
    "details": null
  },
  "meta": null
}
```

```http
GET /api/v1/groups/{id}/join-link

→ 403
{
  "success": false,
  "data": null,
  "error": {
    "code": "FORBIDDEN",
    "message": "Bạn không có quyền thực hiện thao tác này trong nhóm.",
    "details": null
  },
  "meta": null
}
```

```http
PUT /api/v1/groups/{id}/join-link

{
  "enabled": false
}

→ 200
{
  "success": true,
  "data": {
    "code": "6R1W-xlxHY3V04gaiB2-EA",
    "joinUrl": "https://finmate.app/join/6R1W-xlxHY3V04gaiB2-EA",
    "role": "Member",
    "enabled": false
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/group-invitations/accept

{
  "token": "6R1W-xlxHY3V04gaiB2-EA"
}

→ 422
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_INVITATION_INVALID",
    "message": "Lời mời không hợp lệ hoặc đã hết hạn.",
    "details": null
  },
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/join-link/reset

→ 200
{
  "success": true,
  "data": {
    "code": "bRBhspvwy-Br8d3YYB_yhA",
    "joinUrl": "https://finmate.app/join/bRBhspvwy-Br8d3YYB_yhA",
    "role": "Member",
    "enabled": true
  },
  "error": null,
  "meta": null
}
```


## 3. Tìm người và gợi ý

### `GET /users/search?q=&groupId=`

- Khớp **chính xác**: `q` chứa `@` thì là email (chỉ tài khoản **đã xác minh** email); còn lại thử như FinMate ID (không phân biệt hoa thường, được bỏ tiền tố `FM-`). Khớp từng phần hay số điện thoại → danh sách rỗng. **Số điện thoại chưa được hỗ trợ** (tài khoản chưa có trường này).
- `q` phải từ 3 đến 320 ký tự (400 nếu không).
- Có `groupId` thì cần quyền `InviteMembers` ở nhóm đó; người đã trong nhóm bị loại, Không có `groupId` thì chỉ loại chính người gọi.
- Email được che: `m***@gmail.com`.
- **Giới hạn chống dò:** mỗi người gọi tối đa 20 lần mỗi 60 phút (cả lần tìm không ra), vượt thì 429 `USER_SEARCH_RATE_LIMITED`. Hai số này đổi được trong cấu hình hệ thống (`groups.userSearchLimit`, `groups.userSearchWindowMinutes`).

FinMate ID (`finMateId`, dạng `FM-XXXXXXXX`) có trong hồ sơ của chính mình ở `GET /users/me` để người dùng chia sẻ cho người khác.

```http
GET /api/v1/users/search?q=FM-JSW48W4Y&groupId={id}

→ 200
{
  "success": true,
  "data": [
    {
      "userId": "8b01a1ce-4770-41f8-aed8-7320a37e9d84",
      "displayName": "Mai",
      "maskedEmail": "m***@gmail.com",
      "finMateId": "FM-JSW48W4Y"
    }
  ],
  "error": null,
  "meta": null
}
```

```http
GET /api/v1/users/search?q=FM-ZZZZZZZZ

→ 200
{
  "success": true,
  "data": [],
  "error": null,
  "meta": null
}
```


### `GET /groups/{id}/invite-suggestions`

Cần `InviteMembers`. Tối đa 10 người (cấu hình `groups.maxInviteSuggestions`) cùng nhóm khác với người gọi, chưa ở nhóm này; nhiều nhóm chung nhất trước.

```http
GET /api/v1/groups/{id}/invite-suggestions

→ 200
{
  "success": true,
  "data": [
    {
      "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
      "displayName": "Hung",
      "finMateId": "FM-CHVM4WB5",
      "sharedGroupCount": 1
    },
    {
      "userId": "3026b293-a99a-4408-a214-75924c9f549f",
      "displayName": "Lan",
      "finMateId": "FM-EU2SV43Q",
      "sharedGroupCount": 1
    },
    {
      "userId": "8b01a1ce-4770-41f8-aed8-7320a37e9d84",
      "displayName": "Mai",
      "finMateId": "FM-JSW48W4Y",
      "sharedGroupCount": 1
    }
  ],
  "error": null,
  "meta": null
}
```


## 4. Hóa đơn chia

Hóa đơn là sổ ghi "ai trả, ai nợ ai": **không gắn ví nào** và không tạo giao dịch cá nhân. Nó tính toàn bộ số tiền vào chi tiêu, ngân sách và báo cáo tháng của nhóm, đứng tên người trả. Hóa đơn không sửa, không xoá.

`POST /groups/{id}/bills` (quyền `ShareTransactions`; Viewer bị 403):

| Trường | Ghi chú |
|---|---|
| `title` | Bắt buộc, ≤ 200 ký tự |
| `amountCents` | > 0 |
| `paidByUserId` | Phải là thành viên đang hoạt động |
| `occurredAt` | Mặc định bây giờ |
| `splitMode` | `Equal` (mặc định) hoặc `Custom` |
| `splits[{userId, shareCents}]` | Người chia phải là thành viên đang hoạt động, không lặp. `Equal`: chỉ cần `userId`, **`shareCents` bị bỏ qua**, server tự chia và **dồn phần dư cho người trả** (nếu người trả có trong danh sách, không thì cho người đầu danh sách). `Custom`: mỗi phần > 0 và tổng phải bằng `amountCents` |
| `categoryId` / `groupCategoryId` | Tuỳ chọn. `categoryId` là danh mục hệ thống (không phải thu nhập/cho vay/mượn); danh mục nhóm thắng khi có cả hai |
| `note` | ≤ 500 ký tự |

Lỗi: 422 `GROUP_BILL_INVALID` (người trả/người chia không hợp lệ, danh mục sai), 422 `GROUP_BILL_SPLIT_MISMATCH` (tổng ≠ số tiền hoặc có phần ≤ 0). Từ ngưỡng duyệt của nhóm thì trả **202** (mục 6).

```http
POST /api/v1/groups/{id}/bills

{
  "title": "Tiền điện tháng 11",
  "amountCents": 1000000,
  "paidByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
  "occurredAt": "2026-11-20T10:18:44.5934919+00:00",
  "splitMode": "Equal",
  "splits": [
    {
      "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
      "shareCents": 0
    },
    {
      "userId": "3026b293-a99a-4408-a214-75924c9f549f",
      "shareCents": 0
    },
    {
      "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
      "shareCents": 0
    }
  ],
  "groupCategoryId": "24f42432-fb69-4829-96c2-d40ec181acfb",
  "note": "Hóa đơn EVN"
}

→ 201
{
  "success": true,
  "data": {
    "id": "672397f0-e456-4b21-b781-4f8355e158ad",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "title": "Tiền điện tháng 11",
    "amountCents": 1000000,
    "paidByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
    "paidByName": "Lan",
    "occurredAt": "2026-11-20T10:18:44.5934919+00:00",
    "splitMode": "Equal",
    "categoryId": null,
    "groupCategoryId": "24f42432-fb69-4829-96c2-d40ec181acfb",
    "categoryName": null,
    "note": "Hóa đơn EVN",
    "splitCount": 3,
    "splits": [
      {
        "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
        "displayName": "Minh",
        "shareCents": 333333
      },
      {
        "userId": "3026b293-a99a-4408-a214-75924c9f549f",
        "displayName": "Lan",
        "shareCents": 333334
      },
      {
        "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
        "displayName": "Hung",
        "shareCents": 333333
      }
    ],
    "createdAt": "2026-11-20T10:18:44.6876942+00:00"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/bills

{
  "title": "Mua đồ dùng chung",
  "amountCents": 600000,
  "paidByUserId": "c4d3841c-5cd3-474a-8949-243ebc559673",
  "splitMode": "Custom",
  "splits": [
    {
      "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
      "shareCents": 300000
    },
    {
      "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
      "shareCents": 200000
    },
    {
      "userId": "3026b293-a99a-4408-a214-75924c9f549f",
      "shareCents": 100000
    }
  ]
}

→ 201
{
  "success": true,
  "data": {
    "id": "075f9a65-e814-457c-be7f-a1a66ad1cf21",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "title": "Mua đồ dùng chung",
    "amountCents": 600000,
    "paidByUserId": "c4d3841c-5cd3-474a-8949-243ebc559673",
    "paidByName": "Hung",
    "occurredAt": "2026-11-20T10:18:44.882537+00:00",
    "splitMode": "Custom",
    "categoryId": null,
    "groupCategoryId": null,
    "categoryName": null,
    "note": null,
    "splitCount": 3,
    "splits": [
      {
        "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
        "displayName": "Minh",
        "shareCents": 300000
      },
      {
        "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
        "displayName": "Hung",
        "shareCents": 200000
      },
      {
        "userId": "3026b293-a99a-4408-a214-75924c9f549f",
        "displayName": "Lan",
        "shareCents": 100000
      }
    ],
    "createdAt": "2026-11-20T10:18:44.8967851+00:00"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/bills

{
  "title": "Sai tổng",
  "amountCents": 500000,
  "paidByUserId": "c4d3841c-5cd3-474a-8949-243ebc559673",
  "splitMode": "Custom",
  "splits": [
    {
      "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
      "shareCents": 100000
    },
    {
      "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
      "shareCents": 100000
    }
  ]
}

→ 422
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_BILL_SPLIT_MISMATCH",
    "message": "Tổng các phần chia phải bằng số tiền hóa đơn và mỗi phần phải lớn hơn 0.",
    "details": null
  },
  "meta": null
}
```


`GET /groups/{id}/bills?cursor=&limit=` (`ViewOverview`): mới nhất trước, phân trang con trỏ như các danh sách khác (`meta.cursor`, mặc định 20/trang, tối đa 50). Người không có `ViewMemberTransactions` (chế độ `Summary`) chỉ thấy hóa đơn mình trả hoặc có phần. `GET /groups/{id}/bills/{billId}` lấy một hóa đơn (404 nếu không được xem).

```http
GET /api/v1/groups/{id}/bills?limit=20

→ 200
{
  "success": true,
  "data": [
    {
      "id": "075f9a65-e814-457c-be7f-a1a66ad1cf21",
      "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "title": "Mua đồ dùng chung",
      "amountCents": 600000,
      "paidByUserId": "c4d3841c-5cd3-474a-8949-243ebc559673",
      "paidByName": "Hung",
      "occurredAt": "2026-11-20T10:18:44.882537+00:00",
      "splitMode": "Custom",
      "categoryId": null,
      "groupCategoryId": null,
      "categoryName": null,
      "note": null,
      "splitCount": 3,
      "splits": [
        {
          "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
          "displayName": "Minh",
          "shareCents": 300000
        },
        {
          "userId": "3026b293-a99a-4408-a214-75924c9f549f",
          "displayName": "Lan",
          "shareCents": 100000
        },
        {
          "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
          "displayName": "Hung",
          "shareCents": 200000
        }
      ],
      "createdAt": "2026-11-20T10:18:44.896785+00:00"
    },
    {
      "id": "672397f0-e456-4b21-b781-4f8355e158ad",
      "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "title": "Tiền điện tháng 11",
      "amountCents": 1000000,
      "paidByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
      "paidByName": "Lan",
      "occurredAt": "2026-11-20T10:18:44.593491+00:00",
      "splitMode": "Equal",
      "categoryId": null,
      "groupCategoryId": "24f42432-fb69-4829-96c2-d40ec181acfb",
      "categoryName": "Điện nước",
      "note": "Hóa đơn EVN",
      "splitCount": 3,
      "splits": [
        {
          "userId": "3026b293-a99a-4408-a214-75924c9f549f",
          "displayName": "Lan",
          "shareCents": 333334
        },
        {
          "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
          "displayName": "Minh",
          "shareCents": 333333
        },
        {
          "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
          "displayName": "Hung",
          "shareCents": 333333
        }
      ],
      "createdAt": "2026-11-20T10:18:44.687694+00:00"
    }
  ],
  "error": null,
  "meta": {
    "cursor": null,
    "total": null,
    "offset": null,
    "limit": null
  }
}
```

```http
GET /api/v1/groups/{id}/bills/{id}

→ 200
{
  "success": true,
  "data": {
    "id": "075f9a65-e814-457c-be7f-a1a66ad1cf21",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "title": "Mua đồ dùng chung",
    "amountCents": 600000,
    "paidByUserId": "c4d3841c-5cd3-474a-8949-243ebc559673",
    "paidByName": "Hung",
    "occurredAt": "2026-11-20T10:18:44.882537+00:00",
    "splitMode": "Custom",
    "categoryId": null,
    "groupCategoryId": null,
    "categoryName": null,
    "note": null,
    "splitCount": 3,
    "splits": [
      {
        "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
        "displayName": "Minh",
        "shareCents": 300000
      },
      {
        "userId": "3026b293-a99a-4408-a214-75924c9f549f",
        "displayName": "Lan",
        "shareCents": 100000
      },
      {
        "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
        "displayName": "Hung",
        "shareCents": 200000
      }
    ],
    "createdAt": "2026-11-20T10:18:44.896785+00:00"
  },
  "error": null,
  "meta": null
}
```


## 5. Sổ nợ

Nợ **cộng dồn từ đầu** theo mọi hóa đơn và mọi khoản trả nợ, không theo tháng. Toàn số nguyên đồng.

### `GET /groups/{id}/balances` (`ViewOverview`)

- `members[].netCents`: **dương = đang được nợ, âm = đang nợ**; tổng luôn bằng 0. Người đã rời nhóm mà còn nợ vẫn hiện (`isActive: false`).
- `transfers`: danh sách chuyển tiền tối giản (tham lam: người nợ nhiều nhất trả cho người được nợ nhiều nhất), tối đa (số người − 1) lần. **`null` khi nhóm chọn `debtSettlementMethod = Manual`** (người dùng tự quyết ai trả ai).
- `formerMembersNetCents`: phần của người đã xoá cứng tài khoản; không xuất hiện trong `transfers`.

```http
GET /api/v1/groups/{id}/balances

→ 200
{
  "success": true,
  "data": {
    "method": "AiOptimized",
    "members": [
      {
        "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
        "displayName": "Minh",
        "isActive": true,
        "netCents": -633333
      },
      {
        "userId": "8b01a1ce-4770-41f8-aed8-7320a37e9d84",
        "displayName": "Mai",
        "isActive": true,
        "netCents": 0
      },
      {
        "userId": "c4d3841c-5cd3-474a-8949-243ebc559673",
        "displayName": "Hung",
        "isActive": true,
        "netCents": 66667
      },
      {
        "userId": "3026b293-a99a-4408-a214-75924c9f549f",
        "displayName": "Lan",
        "isActive": true,
        "netCents": 566666
      }
    ],
    "transfers": [
      {
        "fromUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
        "toUserId": "3026b293-a99a-4408-a214-75924c9f549f",
        "amountCents": 566666
      },
      {
        "fromUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
        "toUserId": "c4d3841c-5cd3-474a-8949-243ebc559673",
        "amountCents": 66667
      }
    ],
    "formerMembersNetCents": null
  },
  "error": null,
  "meta": null
}
```


### `POST /groups/{id}/settlements`: ghi nhận đã trả nợ

Body `{fromUserId, toUserId, amountCents, note?}` (`note` ≤ 200). Chỉ **người trả**, **người nhận** hoặc **Owner** ghi được (403 với người khác). Cả hai phải là thành viên đang hoạt động, khác nhau, và `amountCents` không vượt số `fromUserId` đang nợ cũng như số `toUserId` đang được nợ, không thì 422 `GROUP_SETTLEMENT_EXCEEDS_DEBT` (hoặc `GROUP_SETTLEMENT_INVALID`). FinMate không chuyển tiền thật, chỉ ghi sổ. Trả 201.

```http
POST /api/v1/groups/{id}/settlements

{
  "fromUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
  "toUserId": "3026b293-a99a-4408-a214-75924c9f549f",
  "amountCents": 100000,
  "note": "Chuyển khoản VCB"
}

→ 201
{
  "success": true,
  "data": {
    "id": "2eac6708-a6ab-421b-829c-372b6fefcd62",
    "fromUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
    "toUserId": "3026b293-a99a-4408-a214-75924c9f549f",
    "amountCents": 100000,
    "note": "Chuyển khoản VCB",
    "createdAt": "2026-11-20T10:18:45.3136036+00:00"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/settlements

{
  "fromUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
  "toUserId": "3026b293-a99a-4408-a214-75924c9f549f",
  "amountCents": 99000000
}

→ 422
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_SETTLEMENT_EXCEEDS_DEBT",
    "message": "Số tiền vượt quá khoản đang nợ.",
    "details": null
  },
  "meta": null
}
```


### Nhắc nợ

- `POST /groups/{id}/members/{userId}/remind-debt`: nhắc một người đang nợ.
- `POST /groups/{id}/remind-all`: nhắc mọi người đang nợ (bỏ qua ai vừa được nhắc, danh sách ở `skipped`).
- Chỉ **người đang được nợ** hoặc người có quyền `ManageSharedTransactions` (Owner/Manager) nhắc được, không thì 403. Nhắc người không nợ → 422 `GROUP_NO_DEBT`.
- Mỗi người chỉ bị nhắc **một lần mỗi 24 giờ** trong mỗi nhóm (cấu hình `groups.debtReminderCooldownHours`). Nhắc lại sớm hơn → **429** `GROUP_REMINDER_COOLDOWN`; `remind-all` chỉ trả 429 khi **không ai** được nhắc.
- Người bị nhắc nhận push + thông báo trong app (`data.kind = group_debt_reminder`, `groupId`); không có số tiền. Người đã tắt cảnh báo nhóm vẫn bị tính một lần nhắc nhưng không nhận push.

```http
POST /api/v1/groups/{id}/members/{id}/remind-debt

→ 200
{
  "success": true,
  "data": {
    "sent": [
      "e6349c5f-3384-4e71-b0c5-90ab2beb3368"
    ],
    "skipped": []
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/members/{id}/remind-debt

→ 429
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_REMINDER_COOLDOWN",
    "message": "Bạn vừa nhắc người này rồi. Hãy thử lại sau.",
    "details": null
  },
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/members/{id}/remind-debt

→ 422
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_NO_DEBT",
    "message": "Người này hiện không nợ ai trong nhóm.",
    "details": null
  },
  "meta": null
}
```


## 6. Duyệt khoản lớn

Owner đặt `requireApprovalAboveCents` ở `PUT /groups/{id}/settings`. Từ ngưỡng đó trở lên (**`>=`**), khoản do người **không phải Owner** tạo sẽ chờ duyệt thay vì có hiệu lực. Áp dụng cho:

- **Hóa đơn chia** (`POST /groups/{id}/bills`).
- **Chi quỹ** và **rút quỹ** (`POST /groups/{id}/funds/{fundId}/entries` với `kind = Expense` hoặc `Withdrawal`). Chi quỹ lấy từ một giao dịch có sẵn (`fromTransactionId`) được so ngưỡng theo số tiền của giao dịch gốc.

Không áp dụng cho: góp quỹ, điều chỉnh quỹ, giao dịch cá nhân chia sẻ vào nhóm (khoản đó đã chi từ ví chủ nó, không lấy từ quỹ nhóm), và mọi khoản do Owner tạo.

Khoản chờ **chưa có**: chưa trừ quỹ, chưa vào ngân sách, báo cáo, sổ nợ hay danh sách hóa đơn. Server trả **202** với body là yêu cầu chờ duyệt (`status: "Pending"`). Gửi lại chi quỹ cùng `clientRequestId` trả lại đúng yêu cầu cũ.

```http
POST /api/v1/groups/{id}/bills

{
  "title": "Đặt cọc phòng",
  "amountCents": 6000000,
  "paidByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
  "splitMode": "Equal",
  "splits": [
    {
      "userId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
      "shareCents": 0
    },
    {
      "userId": "3026b293-a99a-4408-a214-75924c9f549f",
      "shareCents": 0
    }
  ]
}

→ 202
{
  "success": true,
  "data": {
    "id": "e106f2e3-0c22-4fbf-93ef-4c6205fcbb32",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "kind": "Bill",
    "status": "Pending",
    "amountCents": 6000000,
    "title": "Đặt cọc phòng",
    "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
    "requestedByName": "Lan",
    "createdAt": "2026-11-20T10:18:45.5973746+00:00",
    "decidedByUserId": null,
    "decidedAt": null,
    "decisionReason": null,
    "resultId": null
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/funds/{id}/entries

{
  "kind": "Expense",
  "amountCents": 7000000,
  "groupCategoryId": "24f42432-fb69-4829-96c2-d40ec181acfb",
  "merchantName": "Sửa máy giặt",
  "clientRequestId": "6201ad0d-a4f3-463a-910e-509e9d15a220"
}

→ 202
{
  "success": true,
  "data": {
    "id": "68fd73f0-3eb0-4d95-ad40-3523fc0a02f7",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "kind": "FundEntry",
    "status": "Pending",
    "amountCents": 7000000,
    "title": "Sửa máy giặt",
    "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
    "requestedByName": "Lan",
    "createdAt": "2026-11-20T10:18:45.7447837+00:00",
    "decidedByUserId": null,
    "decidedAt": null,
    "decisionReason": null,
    "resultId": null
  },
  "error": null,
  "meta": null
}
```


Lúc gửi server chỉ kiểm quyền và ngưỡng; phần kiểm sâu (thành viên, ví, danh mục) chạy lại **lúc duyệt**. Nếu lúc đó không còn hợp lệ (ví dụ người gửi đã rời nhóm) thì thao tác duyệt báo lỗi và yêu cầu vẫn ở trạng thái chờ để Owner từ chối.

### Danh sách và xử lý

- `GET /groups/{id}/approvals?status=Pending|Approved|Rejected|All&limit=`: mặc định `Pending`, mới nhất trước, tối đa 100. **Owner thấy mọi yêu cầu; người khác chỉ thấy yêu cầu của chính mình.** `status` lạ là 400.
- `POST /groups/{id}/approvals/{approvalId}/approve`: **chỉ Owner** (Manager và chính người gửi đều 403). Khoản được tạo như bình thường dưới tên người gửi; `resultId` trỏ tới hóa đơn hoặc dòng quỹ vừa tạo.
- `POST /groups/{id}/approvals/{approvalId}/reject` `{reason?}` (≤ 200 ký tự): không tạo gì, lưu `decisionReason`.
- Yêu cầu đã xử lý mà duyệt/từ chối lần nữa → 422 `GROUP_APPROVAL_NOT_PENDING`.
- Owner nhận push + thông báo trong app khi có yêu cầu mới (`data.kind = group_approval_req`, `groupId`, `approvalId`); người gửi nhận khi được duyệt hoặc bị từ chối (`group_approval_res`, thêm `approved`). Không có số tiền.

`kind` của yêu cầu: `Bill` hoặc `FundEntry`. `status`: `Pending`, `Approved`, `Rejected`.

```http
GET /api/v1/groups/{id}/approvals?status=Pending

→ 200
{
  "success": true,
  "data": [
    {
      "id": "68fd73f0-3eb0-4d95-ad40-3523fc0a02f7",
      "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "kind": "FundEntry",
      "status": "Pending",
      "amountCents": 7000000,
      "title": "Sửa máy giặt",
      "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
      "requestedByName": "Lan",
      "createdAt": "2026-11-20T10:18:45.744783+00:00",
      "decidedByUserId": null,
      "decidedAt": null,
      "decisionReason": null,
      "resultId": null
    },
    {
      "id": "e106f2e3-0c22-4fbf-93ef-4c6205fcbb32",
      "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "kind": "Bill",
      "status": "Pending",
      "amountCents": 6000000,
      "title": "Đặt cọc phòng",
      "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
      "requestedByName": "Lan",
      "createdAt": "2026-11-20T10:18:45.597374+00:00",
      "decidedByUserId": null,
      "decidedAt": null,
      "decisionReason": null,
      "resultId": null
    }
  ],
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/approvals/{id}/approve

→ 403
{
  "success": false,
  "data": null,
  "error": {
    "code": "FORBIDDEN",
    "message": "Chỉ chủ nhóm mới thực hiện được thao tác này.",
    "details": null
  },
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/approvals/{id}/approve

→ 200
{
  "success": true,
  "data": {
    "id": "e106f2e3-0c22-4fbf-93ef-4c6205fcbb32",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "kind": "Bill",
    "status": "Approved",
    "amountCents": 6000000,
    "title": "Đặt cọc phòng",
    "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
    "requestedByName": "Lan",
    "createdAt": "2026-11-20T10:18:45.597374+00:00",
    "decidedByUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
    "decidedAt": "2026-11-20T10:18:45.9067467+00:00",
    "decisionReason": null,
    "resultId": "86ce483e-40c7-4981-af01-6df18233c04e"
  },
  "error": null,
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/approvals/{id}/approve

→ 422
{
  "success": false,
  "data": null,
  "error": {
    "code": "GROUP_APPROVAL_NOT_PENDING",
    "message": "Yêu cầu này đã được xử lý.",
    "details": null
  },
  "meta": null
}
```

```http
POST /api/v1/groups/{id}/approvals/{id}/reject

{
  "reason": "Để báo giá khác"
}

→ 200
{
  "success": true,
  "data": {
    "id": "68fd73f0-3eb0-4d95-ad40-3523fc0a02f7",
    "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
    "kind": "FundEntry",
    "status": "Rejected",
    "amountCents": 7000000,
    "title": "Sửa máy giặt",
    "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
    "requestedByName": "Lan",
    "createdAt": "2026-11-20T10:18:45.744783+00:00",
    "decidedByUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
    "decidedAt": "2026-11-20T10:18:46.0281262+00:00",
    "decisionReason": "Để báo giá khác",
    "resultId": null
  },
  "error": null,
  "meta": null
}
```

```http
GET /api/v1/groups/{id}/approvals?status=All

→ 200
{
  "success": true,
  "data": [
    {
      "id": "68fd73f0-3eb0-4d95-ad40-3523fc0a02f7",
      "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "kind": "FundEntry",
      "status": "Rejected",
      "amountCents": 7000000,
      "title": "Sửa máy giặt",
      "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
      "requestedByName": "Lan",
      "createdAt": "2026-11-20T10:18:45.744783+00:00",
      "decidedByUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
      "decidedAt": "2026-11-20T10:18:46.028126+00:00",
      "decisionReason": "Để báo giá khác",
      "resultId": null
    },
    {
      "id": "e106f2e3-0c22-4fbf-93ef-4c6205fcbb32",
      "groupId": "6063dea2-d9c7-4da2-9f1e-d18f23d992f2",
      "kind": "Bill",
      "status": "Approved",
      "amountCents": 6000000,
      "title": "Đặt cọc phòng",
      "requestedByUserId": "3026b293-a99a-4408-a214-75924c9f549f",
      "requestedByName": "Lan",
      "createdAt": "2026-11-20T10:18:45.597374+00:00",
      "decidedByUserId": "e6349c5f-3384-4e71-b0c5-90ab2beb3368",
      "decidedAt": "2026-11-20T10:18:45.906746+00:00",
      "decisionReason": null,
      "resultId": "86ce483e-40c7-4981-af01-6df18233c04e"
    }
  ],
  "error": null,
  "meta": null
}
```


## 7. Tổng quan của tôi

`GET /groups/me/summary?year=&month=` (mặc định tháng này, giờ Việt Nam), cộng qua mọi nhóm người dùng đang tham gia:

| Trường | Định nghĩa |
|---|---|
| `totalSpentCents` | Tổng chi của các nhóm trong tháng |
| `activeGroupCount` | Số nhóm đang tham gia |
| `myShareDueCents` | Tổng **phần của chính mình** trong các hóa đơn chia của tháng |
| `myPayableCents` | Tổng số mình **đang nợ** lúc này (cộng dồn, không theo tháng). `null` khi chưa nhóm nào có hóa đơn hay khoản trả nợ |
| `closingDate` | Ngày chốt sổ gần nhất sắp tới trong các nhóm (nhóm không đặt `closingDay` tính là cuối tháng); `null` khi chưa có nhóm |

```http
GET /api/v1/groups/me/summary

→ 200
{
  "success": true,
  "data": {
    "year": 2026,
    "month": 11,
    "totalSpentCents": 7600000,
    "activeGroupCount": 2,
    "myShareDueCents": 3633333,
    "myPayableCents": 3533333,
    "closingDate": "2026-11-30"
  },
  "error": null,
  "meta": null
}
```


## 8. Mã lỗi mới

| Mã | HTTP | Khi nào |
|---|---|---|
| `GROUP_DEFAULT_ACCOUNT_INVALID` | 422 | Ví mặc định khi nhận lời mời không phải của người dùng |
| `GROUP_BILL_INVALID` | 422 | Người trả/người chia không hợp lệ, danh mục sai |
| `GROUP_BILL_SPLIT_MISMATCH` | 422 | Tổng phần chia ≠ số tiền, hoặc có phần ≤ 0 |
| `GROUP_SETTLEMENT_INVALID` | 422 | Hai bên giống nhau hoặc không phải thành viên đang hoạt động |
| `GROUP_SETTLEMENT_EXCEEDS_DEBT` | 422 | Số tiền vượt khoản đang nợ |
| `GROUP_NO_DEBT` | 422 | Nhắc nợ người không đang nợ |
| `GROUP_REMINDER_COOLDOWN` | 429 | Nhắc lại trong vòng 24 giờ |
| `GROUP_APPROVAL_NOT_PENDING` | 422 | Yêu cầu đã được xử lý |
| `USER_SEARCH_RATE_LIMITED` | 429 | Tìm người quá giới hạn |

Mã đã có từ trước vẫn dùng: `GROUP_INVITATION_INVALID`, `GROUP_ALREADY_MEMBER`, `GROUP_TOO_MANY_INVITATIONS`, `FORBIDDEN` (403), `NOT_FOUND` (404), `VALIDATION_ERROR` (400).

```http
GET /api/v1/groups/{id}/balances

→ 404
{
  "success": false,
  "data": null,
  "error": {
    "code": "NOT_FOUND",
    "message": "Group với id '6063dea2-d9c7-4da2-9f1e-d18f23d992f2' không tồn tại.",
    "details": null
  },
  "meta": null
}
```

```http
GET /api/v1/users/search?q=ab

→ 400
{
  "success": false,
  "data": null,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Dữ liệu không hợp lệ.",
    "details": [
      {
        "field": "q",
        "message": "Từ khóa phải từ 3 đến 320 ký tự."
      }
    ]
  },
  "meta": null
}
```


## 9. Chưa làm (để FE khỏi chờ)

- **Ảnh đại diện nhóm** (`POST/DELETE /groups/{id}/avatar`): chưa có chỗ lưu ảnh. `avatarUrl` luôn `null`.
- **Job tóm tắt tuần** và **thông báo khi có giao dịch mới**: hai cờ `weeklySummary` và `notifyOnNewTransaction` mới chỉ được lưu.
- **Xuất file** (`GET /groups/{id}/export`): chưa làm, chờ duyệt thư viện xlsx/pdf và font tiếng Việt. Phase 91 (2026-11-25) có xuất dữ liệu **cá nhân** (`docs/data-export-api.md`): gói của mỗi người chỉ có phần của chính họ trong nhóm; xuất dữ liệu nhóm vẫn ở REQUIREMENTS 13.4.
- **Tìm theo số điện thoại**: tài khoản chưa có số điện thoại.
- **Vai trò Thủ quỹ**: không có gì ở backend; giữ vai trò `Manager` và hiển thị nhãn "Thủ quỹ". Manager đã có `ManageFunds`; riêng việc duyệt khoản lớn chỉ Owner làm được.
- `splitCount`, `isSplit`, `paidFromFund` trên giao dịch nhóm và `unsplitTransactionCount` trên `GET /groups`: chưa làm; `splitCount` hiện chỉ có trên hóa đơn.
- Tên miền `finmate.app` và deep link `/join/<token>`: backend chỉ tạo URL.
