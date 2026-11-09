# API cộng đồng

Nơi người dùng chia sẻ kinh nghiệm chi tiêu, khó khăn tài chính, câu hỏi, và quán ăn hay deal tốt. Mọi đường dẫn nằm dưới `/api/v1/community`, cần JWT, và trả 422 `COMMUNITY_DISABLED` khi cờ `features.community` tắt (mặc định tắt). Kiểm duyệt ở [`ops-api.md`](ops-api.md), quy trình ở [`ops-runbook.md`](ops-runbook.md).

## Quy tắc

- **Ẩn danh là mặc định.** Không gửi `isAnonymous` thì vẫn ẩn danh, và mỗi lần đăng đều bắt đầu từ ẩn danh. Đổi được sau khi đăng.
- **Không có id tác giả** trong bất kỳ response nào, kể cả bài công khai tên. Chỉ có `author { isAnonymous, displayName }` và `isMine`. Bình luận không có nhãn "người đăng bài".
- **Ngày không kèm giờ:** `createdOn` chỉ có ngày theo giờ Việt Nam.
- **Chỉ bài chủ đề `places` có quán và khu vực.** Bài chủ đề khác gửi kèm quán, khu vực, giá hoặc `transactionId` bị từ chối, và hiện trên toàn quốc.
- **Không có đường đính kèm giao dịch, ngân sách hay số dư.** `transactionId` chỉ dùng để xét nhãn "đã chi ở đây": không đọc số tiền, không lưu lại mã. Không đạt thì không có nhãn, không báo lỗi.
- **Nội dung công khai nên phải sạch:** tiêu đề, bài, bình luận, tên quán, địa chỉ bị từ chối nếu có đường dẫn, số điện thoại, email, chuỗi từ 9 chữ số liền hoặc từ cấm. Bộ lọc chỉ giảm bớt; viết lách lách luật vẫn lọt, phần còn lại nhờ báo cáo và nhân viên.
- **Đăng cần đồng ý riêng:** `POST /users/me/consents` với `{ "kind": "community", "version": 1 }`. Xem thì không cần.

## Chủ đề và khu vực

| Mã | Tên | Gắn quán và khu vực |
|---|---|---|
| `places` | Quán ngon, deal tốt | Bắt buộc |
| `experience` | Kinh nghiệm chi tiêu | Không |
| `difficulty` | Khó khăn tài chính | Không |
| `question` | Hỏi đáp | Không |

Chủ đề, thẻ, khu vực, từ cấm, từ khóa ăn uống và các giới hạn nằm ở nhóm `community` trong `PUT /admin/system-config`. Không xóa được chủ đề `places`. Khu vực là tên quen gọi, không phải đơn vị hành chính; mặc định có 34 tỉnh, thành và khu vực của Hà Nội và TP. Hồ Chí Minh.

## Giới hạn mặc định

| Việc | Giới hạn |
|---|---|
| Bài | 5 mỗi 24 giờ, tính cả bài đã xóa; chia sẻ lại quán cũ là sửa bài nên không tính |
| Bình luận | 30 mỗi 24 giờ, tính cả bình luận đã xóa; tối đa 300 ký tự |
| Ảnh | 10 mỗi 24 giờ, tính cả ảnh đã xóa; 3 ảnh mỗi bài; toàn hệ thống tối đa 500 ảnh chờ duyệt |
| Vote | 60 mỗi giờ |
| Báo cáo | 20 mỗi 24 giờ |
| Tiêu đề, nội dung | 120 và 2000 ký tự; tối đa 5 thẻ |

## Hồ sơ, chủ đề, khu vực

| API | Việc |
|---|---|
| `GET /topics`, `GET /regions` | Danh sách chủ đề, khu vực |
| `GET /me` | `{ provinceCode, areaCode, promptsEnabled, consentGranted, mutedUntil }` |
| `PATCH /me` | `{ provinceCode?, areaCode?, promptsEnabled? }` |

## Bài

### `POST /posts`

```json
{ "topic": "difficulty", "title": "Nợ thẻ tín dụng", "body": "…", "tags": ["no_nan"], "isAnonymous": true }
```

Chủ đề `places` thêm `placeId`, hoặc `placeName` + `provinceCode` + `areaCode?` + `addressText?`, cùng `priceCents?` và `transactionId?`. `body` có thể rỗng: chạm một lần chỉ tính vào điểm quán, không hiện ở bảng tin và không bình luận được.

Trả 201 khi tạo mới. Với `places`, chia sẻ lại quán đã có bài của mình thì cập nhật bài đó và trả 200.

```json
{ "id": "…", "topic": "places", "title": null, "body": "Cơm ngon", "tags": ["gia_tot"],
  "author": { "isAnonymous": true, "displayName": null }, "isMine": true,
  "createdOn": "2026-11-06", "edited": false, "voteCount": 0, "commentCount": 0, "votedByMe": false,
  "spentHereVerified": true, "priceCents": 30000,
  "place": { "id": "…", "name": "Quán X", "provinceCode": "ho-chi-minh", "areaCode": "quan-7", "addressText": null },
  "images": [], "status": "visible" }
```

`status` chỉ có với bài của chính mình (`visible`, `auto_hidden`, `removed`).

### Các API khác

| API | Việc |
|---|---|
| `GET /posts?topic&provinceCode&areaCode&tag&sort=new\|top&mine&cursor&limit` | Bảng tin. Bộ lọc khu vực chỉ áp cho bài có quán. `top` xếp theo vote trong 30 ngày. `mine=true` gồm cả bài chạm một lần và bài đang bị ẩn của mình, để người dùng xóa được |
| `GET /posts/{id}` | Một bài. Người khác nhận 404 nếu bài đã ẩn |
| `PATCH /posts/{id}` | `{ title?, body?, tags?, isAnonymous?, priceCents? }`. Null là giữ nguyên, `priceCents = 0` là bỏ giá. Bài đang bị ẩn trả 422 `COMMUNITY_POST_NOT_EDITABLE` |
| `DELETE /posts/{id}` | Xóa ngay nội dung, giá và dữ liệu ảnh. 204 |
| `PUT`, `DELETE /posts/{id}/vote` | Gọi lại vẫn an toàn. Trả `{ voteCount, votedByMe }`. Vote bài của mình trả 422 `COMMUNITY_OWN_POST` |
| `POST /posts/{id}/report`, `POST /comments/{id}/report` | `{ "reason": "scam\|personal_info\|harassment\|sexual\|spam\|self_harm\|other" }`. 204. Báo cáo lại không tính thêm |

## Quán

| API | Việc |
|---|---|
| `GET /places?provinceCode&areaCode&tag&maxPriceCents&q&cursor&limit` | Xếp theo điểm = chi thật × 3 + vote + số chia sẻ, giảm dần theo thời gian. `typicalPriceCents` là trung vị. `q` không phân biệt dấu |
| `GET /places/{id}` | Tóm tắt và các bài có nội dung |

Tính khi đọc, không có cột đếm tính sẵn. Chỉ xét 2000 lượt chia sẻ mới nhất khớp bộ lọc khu vực và tên, nên cần xem lại khi dữ liệu rất lớn.

## Bình luận

| API | Việc |
|---|---|
| `GET /posts/{id}/comments?cursor&limit` | Cũ trước. `{ id, body, author, isMine, createdOn, edited }` |
| `POST /posts/{id}/comments` | `{ body, isAnonymous? }`. 201 |
| `PATCH`, `DELETE /comments/{id}` | Chỉ của mình. Xóa thì xóa luôn nội dung |

## Ảnh

| API | Việc |
|---|---|
| `POST /posts/{id}/images` | Multipart `file`, tối đa 5 MB. 201 kèm `{ id, width, height, status: "pending" }` |
| `DELETE /posts/{id}/images/{imageId}` | Xóa dữ liệu ảnh ngay. 204 |
| `GET /images/{imageId}` | `image/jpeg`. Người khác chỉ tải được ảnh đã duyệt của bài họ được xem, còn lại 404 |

Nhận JPEG, PNG, WebP, nhận dạng theo nội dung file, tối đa 40 MP. **HEIC/HEIF bị từ chối: app phải đổi sang JPEG trước khi gửi.** Mọi ảnh được vẽ lại thành JPEG, cạnh dài tối đa 1600 px, nên EXIF và GPS không còn; chiều xoay được áp vào điểm ảnh trước. Ảnh chỉ hiện với tác giả cho tới khi `Staff` duyệt.

## Hỏi chia sẻ sau giao dịch

`GET /share-prompt?transactionId=` trả `{ shouldPrompt, suggestedPlaceName, suggestedPriceCents, existingPlaces[] }`. App gọi **một lần** sau khi lưu hoặc xác nhận giao dịch: trả lời "có" được tính là đã hỏi.

Chỉ hỏi khi giao dịch là chi tiêu đã xác nhận, không phải cho vay, tối đa 300.000đ, là ăn uống (danh mục `food`, hoặc từ khóa trong tên quán, mô tả, tên danh mục tự đặt), công tắc nhắc đang bật, chưa có bài về quán đó, tối đa một lần mỗi ngày và 30 ngày mỗi quán. Redis lỗi thì không hỏi.

Không có gì được đăng tự động. Với thông báo chuyển khoản, `suggestedPlaceName` có thể là tên người thật, nên màn hình phải cho sửa và xác nhận trước khi đăng.

## Mã lỗi

| Mã | Khi nào |
|---|---|
| `COMMUNITY_DISABLED` | Cờ tắt |
| `COMMUNITY_CONSENT_REQUIRED` | Chưa đồng ý |
| `COMMUNITY_MUTED` | Đang bị cấm đăng |
| `COMMUNITY_TOPIC_UNKNOWN`, `COMMUNITY_REGION_UNKNOWN` | Mã không có trong cấu hình |
| `COMMUNITY_PLACE_REQUIRED`, `COMMUNITY_PLACE_NOT_ALLOWED` | Thiếu quán, hoặc chủ đề không gắn quán |
| `COMMUNITY_CONTENT_NOT_ALLOWED` | Vi phạm bộ lọc nội dung |
| `COMMUNITY_DISPLAY_NAME_NOT_ALLOWED` | Tắt ẩn danh khi tên hiển thị có thông tin liên hệ |
| `COMMUNITY_LIMIT_REACHED` | Vượt giới hạn |
| `COMMUNITY_IMAGE_INVALID`, `COMMUNITY_IMAGE_QUEUE_FULL` | Ảnh không dùng được, hoặc hàng đợi duyệt đã đầy |
| `COMMUNITY_POST_NOT_EDITABLE`, `COMMUNITY_COMMENTS_NOT_ALLOWED`, `COMMUNITY_OWN_POST` | Bài đang ẩn, bài không nhận bình luận, thao tác lên nội dung của mình |
| `COMMUNITY_INVALID_INPUT`, `COMMUNITY_CONFLICT` | Sai dữ liệu, hoặc thao tác đồng thời |
